using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexi.Data.Context;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;

namespace Nexi.Services
{
    public class AIModelService : IAIModelService
    {
        private readonly NexiDbContext _context;
        private readonly ILogger<AIModelService> _logger;
        private readonly HttpClient _httpClient;
        private readonly IDbContextFactory<NexiDbContext> _contextFactory;
        private readonly ILlamaSharpService _llamaSharpService;
        private readonly string _modelsDirectory;
        private readonly Dictionary<string, CancellationTokenSource> _downloadCancellationTokens = new();
        private readonly Dictionary<string, Task> _downloadTasks = new();

        public AIModelService(
            NexiDbContext context, 
            ILogger<AIModelService> logger,
            IDbContextFactory<NexiDbContext> contextFactory,
            ILlamaSharpService llamaSharpService)
        {
            _context = context;
            _logger = logger;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromHours(1) // Set a long timeout for large file downloads
            };
            _contextFactory = contextFactory;
            _llamaSharpService = llamaSharpService;
            
            // Create models directory if it doesn't exist
            _modelsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Nexi", "Models");
            
            if (!Directory.Exists(_modelsDirectory))
            {
                Directory.CreateDirectory(_modelsDirectory);
            }
        }

        public async Task<IEnumerable<AIModelData>> GetAllModelsAsync()
        {
            return await _context.AIModels.ToListAsync();
        }

        public async Task<AIModelData?> GetModelAsync(string id)
        {
            return await _context.AIModels.FirstOrDefaultAsync(m => m.Id == id);
        }

        public async Task<AIModelData> UpdateModelStatusAsync(string id, ModelStatus status)
        {
            var model = await _context.AIModels.FirstOrDefaultAsync(m => m.Id == id);
            if (model == null)
            {
                throw new ArgumentException($"Model with ID {id} not found");
            }

            model.Status = status;
            model.LastModifiedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return model;
        }

        public async Task<bool> DeleteModelAsync(string id)
        {
            var model = await _context.AIModels.FirstOrDefaultAsync(m => m.Id == id);
            if (model == null)
            {
                return false;
            }

            _context.AIModels.Remove(model);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<AIModelData> StartDownloadModelAsync(string id)
        {
            var model = await _context.AIModels.FirstOrDefaultAsync(m => m.Id == id);
            if (model == null)
            {
                throw new ArgumentException($"Model with ID {id} not found");
            }

            model.Status = ModelStatus.Downloading;
            model.LastModifiedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return model;
        }

        public async Task<AIModelData> DownloadModelFromHuggingFaceAsync(string id, string repoId, IProgress<(string, int)>? progress = null)
        {
            // This method is now deprecated in favor of DownloadGgufModelAsync
            throw new NotImplementedException("This method is deprecated. Use DownloadGgufModelAsync instead.");
        }

        public async Task<bool> IsModelDownloadedAsync(string id)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var model = await context.AIModels.FirstOrDefaultAsync(m => m.Id == id);
            return model != null && model.Status == ModelStatus.Downloaded;
        }

        public async Task<string> GetModelLocalPathAsync(string id)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var model = await context.AIModels.FirstOrDefaultAsync(m => m.Id == id);
            return model?.LocalPath ?? string.Empty;
        }

        public async Task<bool> RunModelInferenceAsync(string id, string prompt, Action<string> onTokenGenerated)
        {
            try
            {
                // Get model path
                var modelPath = await GetModelLocalPathAsync(id);
                if (string.IsNullOrEmpty(modelPath))
                {
                    _logger.LogError($"Model {id} not found or not downloaded");
                    return false;
                }

                // Get user settings for LlamaSharp
                using var context = await _contextFactory.CreateDbContextAsync();
                var settings = await context.UserSettings.FirstOrDefaultAsync();
                if (settings == null)
                {
                    _logger.LogError("User settings not found");
                    return false;
                }

                // Initialize LlamaSharp model
                var initialized = await _llamaSharpService.InitializeModelAsync(
                    modelPath,
                    settings.ContextSize,
                    settings.UseGPU ? settings.GpuLayerCount : 0);

                if (!initialized)
                {
                    _logger.LogError($"Failed to initialize LlamaSharp model {id}");
                    return false;
                }

                // Generate response
                await _llamaSharpService.GenerateResponseAsync(prompt, onTokenGenerated);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error running inference for model {id}");
                return false;
            }
        }

        public async Task<AIModelData> DownloadGgufModelAsync(string modelId, string downloadUrl, IProgress<(string, int)>? progress = null)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            
            // Check if model already exists
            var existingModel = await context.AIModels.FirstOrDefaultAsync(m => m.Id == modelId);
            if (existingModel != null && existingModel.Status == ModelStatus.Downloaded)
            {
                _logger.LogWarning($"Model {modelId} is already downloaded");
                return existingModel;
            }

            // Extract model name from the URL
            var fileName = Path.GetFileName(new Uri(downloadUrl).AbsolutePath);
            var modelName = fileName;
            
            // Create or update model entry
            if (existingModel == null)
            {
                existingModel = new AIModelData
                {
                    Id = modelId,
                    Name = modelName,
                    Description = "GGUF model from HuggingFace",
                    Version = "1.0",
                    Status = ModelStatus.Downloading,
                    RepositoryId = modelId, // Use modelId as repositoryId for direct downloads
                    CreatedAt = DateTime.UtcNow,
                    LastModifiedAt = DateTime.UtcNow
                };
                context.AIModels.Add(existingModel);
            }
            else
            {
                existingModel.Status = ModelStatus.Downloading;
                existingModel.LastModifiedAt = DateTime.UtcNow;
            }
            
            await context.SaveChangesAsync();

            // Create model directory
            var modelDirectory = Path.Combine(_modelsDirectory, modelId);
            if (!Directory.Exists(modelDirectory))
            {
                Directory.CreateDirectory(modelDirectory);
            }

            // Download the model
            var localFilePath = Path.Combine(modelDirectory, fileName);
            
            // Check if we already have a download in progress for this model
            if (_downloadTasks.TryGetValue(modelId, out var existingTask) && !existingTask.IsCompleted)
            {
                _logger.LogWarning($"Download for model {modelId} is already in progress");
                return existingModel;
            }

            // Create cancellation token
            var cts = new CancellationTokenSource();
            _downloadCancellationTokens[modelId] = cts;

            // Start download task
            var downloadTask = Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation($"Starting download of model {modelId} from {downloadUrl}");
                    
                    // Send a GET request and start downloading the file
                    using (HttpResponseMessage response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token))
                    {
                        response.EnsureSuccessStatusCode();

                        long? totalBytes = response.Content.Headers.ContentLength;
                        long totalBytesRead = 0;
                        byte[] buffer = new byte[8192]; // 8 KB buffer
                        int bytesRead;
                        bool canReportProgress = totalBytes.HasValue;

                        using (Stream contentStream = await response.Content.ReadAsStreamAsync(cts.Token),
                                      fileStream = new FileStream(localFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cts.Token)) != 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead, cts.Token);
                                totalBytesRead += bytesRead;

                                // Report progress if total file size is known
                                if (canReportProgress && totalBytes.HasValue)
                                {
                                    int progressPercentage = (int)((float)totalBytesRead / totalBytes.Value * 100);
                                    progress?.Report((fileName, progressPercentage));
                                    _logger.LogDebug($"Downloading {fileName}: {progressPercentage}%");
                                }
                            }
                        }

                        // Update model status to downloaded
                        using var updateContext = await _contextFactory.CreateDbContextAsync();
                        var model = await updateContext.AIModels.FirstOrDefaultAsync(m => m.Id == modelId);
                        if (model != null)
                        {
                            model.Status = ModelStatus.Downloaded;
                            model.LocalPath = localFilePath;
                            model.Size = FormatFileSize(totalBytesRead);
                            model.DownloadedDate = DateTime.UtcNow;
                            model.LastModifiedAt = DateTime.UtcNow;
                            await updateContext.SaveChangesAsync();
                        }

                        _logger.LogInformation($"Successfully downloaded model {modelId} to {localFilePath}");
                        return model;
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning($"Download of model {modelId} was cancelled");
                    
                    // Update model status to not downloaded if cancelled
                    using var updateContext = await _contextFactory.CreateDbContextAsync();
                    var model = await updateContext.AIModels.FirstOrDefaultAsync(m => m.Id == modelId);
                    if (model != null)
                    {
                        model.Status = ModelStatus.NotDownloaded;
                        model.LastModifiedAt = DateTime.UtcNow;
                        await updateContext.SaveChangesAsync();
                    }
                    
                    // Delete partial file
                    if (File.Exists(localFilePath))
                    {
                        File.Delete(localFilePath);
                    }
                    
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error downloading model {modelId}");
                    
                    // Update model status to error
                    using var updateContext = await _contextFactory.CreateDbContextAsync();
                    var model = await updateContext.AIModels.FirstOrDefaultAsync(m => m.Id == modelId);
                    if (model != null)
                    {
                        model.Status = ModelStatus.Error;
                        model.LastModifiedAt = DateTime.UtcNow;
                        await updateContext.SaveChangesAsync();
                    }
                    
                    // Delete partial file
                    if (File.Exists(localFilePath))
                    {
                        File.Delete(localFilePath);
                    }
                    
                    throw;
                }
                finally
                {
                    // Clean up
                    _downloadCancellationTokens.Remove(modelId);
                    _downloadTasks.Remove(modelId);
                }
            });

            _downloadTasks[modelId] = downloadTask;
            
            return existingModel;
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        public Task<IEnumerable<HuggingFaceModelInfo>> SearchHuggingFaceModelsAsync(string query)
        {
            throw new NotImplementedException("This method should be called on the HuggingFaceService");
        }

        public Task<IEnumerable<HuggingFaceModelFile>> GetModelFilesAsync(string modelId)
        {
            throw new NotImplementedException("This method should be called on the HuggingFaceService");
        }
        
        public bool CancelDownload(string modelId)
        {
            if (_downloadCancellationTokens.TryGetValue(modelId, out var cts))
            {
                _logger.LogInformation($"Cancelling download for model {modelId}");
                cts.Cancel();
                return true;
            }
            
            _logger.LogWarning($"No active download found for model {modelId}");
            return false;
        }
    }
}