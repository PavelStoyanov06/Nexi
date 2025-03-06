using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexi.Data.Context;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace Nexi.Services
{
    public class AIModelService : IAIModelService
    {
        private readonly ILogger<AIModelService> _logger;
        private readonly HttpClient _httpClient;
        private readonly IDbContextFactory<NexiDbContext> _contextFactory;
        private readonly ILlamaSharpService _llamaSharpService;
        private readonly string _modelsDirectory;
        private readonly Dictionary<string, CancellationTokenSource> _downloadCancellationTokens = new();
        private readonly Dictionary<string, Task> _downloadTasks = new();
        
        // Semaphore to prevent multiple model initializations at once
        private readonly SemaphoreSlim _modelInitSemaphore = new SemaphoreSlim(1, 1);

        public AIModelService(
            ILogger<AIModelService> logger,
            IDbContextFactory<NexiDbContext> contextFactory,
            ILlamaSharpService llamaSharpService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _llamaSharpService = llamaSharpService ?? throw new ArgumentNullException(nameof(llamaSharpService));
            _httpClient = new HttpClient();
            
            // Set up models directory
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Nexi");
            
            _modelsDirectory = Path.Combine(appDataPath, "Models");
            
            if (!Directory.Exists(_modelsDirectory))
            {
                Directory.CreateDirectory(_modelsDirectory);
            }
        }

        public async Task<IEnumerable<AIModelData>> GetAllModelsAsync()
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                return await context.AIModels.ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all models");
                return Enumerable.Empty<AIModelData>();
            }
        }

        public async Task<AIModelData?> GetModelAsync(string id)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                return await context.AIModels.FirstOrDefaultAsync(m => m.Id == id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving model with ID {ModelId}", id);
                return null;
            }
        }

        public async Task<AIModelData> UpdateModelStatusAsync(string id, ModelStatus status)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var model = await context.AIModels.FirstOrDefaultAsync(m => m.Id == id);
                
                if (model == null)
                {
                    throw new KeyNotFoundException($"Model with ID {id} not found");
                }
                
                model.Status = status;
                model.LastModifiedAt = DateTime.UtcNow;
                
                await context.SaveChangesAsync();
                return model;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating model status for ID {ModelId}", id);
                throw;
            }
        }

        public async Task<bool> DeleteModelAsync(string id)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var model = await context.AIModels.FirstOrDefaultAsync(m => m.Id == id);
                
                if (model == null)
                {
                    return false;
                }
                
                // Cancel any ongoing download
                CancelDownload(id);
                
                // Delete the model directory if it exists
                string modelDir = Path.Combine(_modelsDirectory, id);
                if (Directory.Exists(modelDir))
                {
                    Directory.Delete(modelDir, true);
                }
                
                context.AIModels.Remove(model);
                await context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting model with ID {ModelId}", id);
                return false;
            }
        }

        public async Task<AIModelData> StartDownloadModelAsync(string id)
        {
            try
            {
                var model = await GetModelAsync(id);
                
                if (model == null)
                {
                    throw new KeyNotFoundException($"Model with ID {id} not found");
                }
                
                if (model.Status == ModelStatus.Downloading)
                {
                    return model;
                }
                
                await UpdateModelStatusAsync(id, ModelStatus.Downloading);
                return model;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting model download for ID {ModelId}", id);
                throw;
            }
        }

        public async Task<AIModelData> DownloadModelFromHuggingFaceAsync(string id, string repoId, IProgress<(string, int)>? progress = null)
        {
            // This method is now deprecated in favor of DownloadGgufModelAsync
            throw new NotImplementedException("This method is deprecated. Use DownloadGgufModelAsync instead.");
        }

        public async Task<bool> IsModelDownloadedAsync(string id)
        {
            var model = await GetModelAsync(id);
            return model?.Status == ModelStatus.Downloaded;
        }

        public async Task<string> GetModelLocalPathAsync(string id)
        {
            try
            {
                var model = await GetModelAsync(id);
                
                if (model == null)
                {
                    throw new KeyNotFoundException($"Model with ID {id} not found");
                }
                
                if (string.IsNullOrEmpty(model.LocalPath))
                {
                    throw new InvalidOperationException($"Model {id} does not have a local path set");
                }
                
                return model.LocalPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting local path for model ID {ModelId}", id);
                throw;
            }
        }

        public async Task<string> RunInferenceAsync(string modelId, string prompt, Action<string> onTokenGenerated, CancellationToken cancellationToken = default)
        {
            try
            {
                var model = await GetModelAsync(modelId);
                if (model == null)
                {
                    _logger.LogError("Model with ID {ModelId} not found", modelId);
                    onTokenGenerated?.Invoke("[Error: Model not found]");
                    return "Error: Model not found";
                }

                if (string.IsNullOrEmpty(model.LocalPath) || !File.Exists(model.LocalPath))
                {
                    _logger.LogError("Model file not found at path: {Path}", model.LocalPath);
                    onTokenGenerated?.Invoke("[Error: Model file not found]");
                    return "Error: Model file not found";
                }

                // Check if the native library exists before attempting to initialize the model
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string runtimesDir = Path.Combine(baseDir, "runtimes");
                string platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
                                 RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
                                 RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" : "unknown";
                string arch = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "x64" :
                             RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x86";
                string nativeLibPath = Path.Combine(runtimesDir, $"{platform}-{arch}", "native");
                string libraryName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "llama.dll" :
                                    RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "libllama.so" :
                                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "libllama.dylib" : "unknown";
                
                string fullLibPath = Path.Combine(nativeLibPath, libraryName);
                bool libraryExists = File.Exists(fullLibPath);
                
                // Also check subdirectories
                if (!libraryExists)
                {
                    string[] subDirs = { "avx", "avx2", "avx512", "noavx" };
                    foreach (var subDir in subDirs)
                    {
                        string subDirPath = Path.Combine(nativeLibPath, subDir);
                        string subLibPath = Path.Combine(subDirPath, libraryName);
                        if (File.Exists(subLibPath))
                        {
                            libraryExists = true;
                            break;
                        }
                    }
                }
                
                if (!libraryExists)
                {
                    _logger.LogError("Native library not found at expected path: {Path} or in subdirectories", fullLibPath);
                    onTokenGenerated?.Invoke("[Error: Native library not found. Please ensure LlamaSharp native libraries are installed correctly.]");
                    return "Error: Native library not found";
                }

                // Get user settings for context size and GPU layers
                using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);
                var settings = await dbContext.UserSettings.FirstOrDefaultAsync(cancellationToken);
                int contextSize = settings?.ContextSize ?? 1024;
                int gpuLayerCount = settings?.UseGPU == true ? settings.GpuLayerCount : 0;

                _logger.LogInformation("Running inference with model {ModelId}, contextSize={ContextSize}, gpuLayerCount={GpuLayerCount}", 
                    modelId, contextSize, gpuLayerCount);

                // Try to initialize the model with GPU support first
                bool initialized = false;
                Exception? lastException = null;
                
                try
                {
                    initialized = await _llamaSharpService.InitializeModelAsync(model.LocalPath, contextSize, gpuLayerCount);
                }
                catch (DllNotFoundException dllEx)
                {
                    _logger.LogError(dllEx, "Native library not found or could not be loaded");
                    lastException = dllEx;
                    
                    // Don't retry if the DLL is missing
                    onTokenGenerated?.Invoke("[Error: Native library not found or could not be loaded. Please ensure LlamaSharp native libraries are installed correctly.]");
                    return "Error: Native library not found or could not be loaded";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error initializing model with GPU layers: {Message}", ex.Message);
                    lastException = ex;
                }
                
                // If GPU initialization failed, try CPU-only mode
                if (!initialized && gpuLayerCount > 0)
                {
                    _logger.LogWarning("GPU initialization failed, falling back to CPU-only mode");
                    onTokenGenerated?.Invoke("[Warning: GPU initialization failed, falling back to CPU-only mode]");
                    
                    try
                    {
                        initialized = await _llamaSharpService.InitializeModelAsync(model.LocalPath, contextSize, 0);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error initializing model in CPU-only mode: {Message}", ex.Message);
                        lastException = ex;
                    }
                }
                
                if (!initialized)
                {
                    string errorMessage = lastException != null 
                        ? $"Error initializing model: {lastException.Message}" 
                        : "Failed to initialize model for unknown reason";
                    
                    _logger.LogError(errorMessage);
                    onTokenGenerated?.Invoke($"[Error: {errorMessage}]");
                    return errorMessage;
                }

                return await _llamaSharpService.GenerateResponseAsync(prompt, onTokenGenerated);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during inference: {Message}", ex.Message);
                onTokenGenerated?.Invoke($"[Error: {ex.Message}]");
                return $"Error during inference: {ex.Message}";
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

        // Implement the interface method
        public async Task<bool> RunModelInferenceAsync(string id, string prompt, Action<string> onTokenGenerated)
        {
            if (string.IsNullOrEmpty(prompt))
            {
                _logger.LogWarning("Prompt is null or empty");
                return false;
            }
            
            // Call the new implementation and convert the result
            string result = await RunInferenceAsync(id, prompt, onTokenGenerated);
            
            // Return true if the result doesn't start with "Error:"
            return !result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase);
        }
    }
}