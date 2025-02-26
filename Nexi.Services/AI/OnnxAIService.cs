using Microsoft.Extensions.Logging;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using SharpCompress.Archives;
using SharpCompress.Common;
using Microsoft.ML.OnnxRuntime;

namespace Nexi.Services.AI
{
    public class OnnxAIService : IAIService, IDisposable
    {
        private readonly ILogger<OnnxAIService> _logger;
        private readonly IAIModelService _modelService;
        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, InferenceSession> _loadedModels = new();
        private readonly string _modelsBasePath;
        private bool _disposed;

        public event EventHandler<string>? OnInferenceProgress;
        public event EventHandler<Exception>? OnError;

        public OnnxAIService(
            ILogger<OnnxAIService> logger,
            IAIModelService modelService)
        {
            _logger = logger;
            _modelService = modelService;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(30); // Long timeout for large downloads

            // Set up the models directory in the user's app data folder
            _modelsBasePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Nexi", "Models");

            // Ensure directory exists
            Directory.CreateDirectory(_modelsBasePath);
        }

        public bool IsModelLoaded(string modelId) => _loadedModels.ContainsKey(modelId);

        public async Task<AIResponse> GetCompletionAsync(string prompt, AIRequestOptions options)
        {
            try
            {
                RaiseInferenceProgress("Preparing inference...");

                // Load the model if it's not already loaded
                if (!_loadedModels.TryGetValue(options.ModelId, out var session))
                {
                    await LoadModelAsync(options.ModelId);
                    if (!_loadedModels.TryGetValue(options.ModelId, out session))
                    {
                        throw new InvalidOperationException($"Failed to load model {options.ModelId}");
                    }
                }

                // In a real implementation, we would use the ONNX model to generate text
                // This is a simplified version that just echoes the prompt
                RaiseInferenceProgress("Generating response...");

                // Simulate some delay for inference
                await Task.Delay(500);

                var response = new AIResponse
                {
                    Text = $"This is a simulated response to: {prompt}\n\nIn a real implementation, this would use the loaded ONNX model to generate text based on the prompt and the specified options (temperature: {options.Temperature}, max tokens: {options.MaxTokens})."
                };

                RaiseInferenceProgress("Response generated successfully");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during text completion");
                OnError?.Invoke(this, ex);
                throw;
            }
        }

        public async Task<AIResponse> GetCompletionWithHistoryAsync(
            IEnumerable<(bool IsUser, string Message)> history,
            string prompt,
            AIRequestOptions options)
        {
            try
            {
                RaiseInferenceProgress("Preparing inference with context...");

                // Format the conversation history into a single context string
                var formattedHistory = FormatConversationHistory(history);
                var fullPrompt = $"{formattedHistory}\nUser: {prompt}\nAssistant:";

                // Use the base completion method with the full prompt
                return await GetCompletionAsync(fullPrompt, options);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during text completion with history");
                OnError?.Invoke(this, ex);
                throw;
            }
        }

        // Update for OnnxAIService.cs - DownloadModelAsync method

        public async Task DownloadModelAsync(string modelId, IProgress<double>? progress = null)
        {
            try
            {
                // Get model info
                var model = await _modelService.GetModelAsync(modelId);
                if (model == null)
                {
                    throw new KeyNotFoundException($"Model {modelId} not found");
                }

                // Get model info with download URL
                var modelInfo = await _modelService.GetModelInfoAsync(modelId);
                if (modelInfo == null || string.IsNullOrEmpty(modelInfo.DownloadUrl))
                {
                    throw new InvalidOperationException($"Download URL not found for model {modelId}");
                }

                RaiseInferenceProgress($"Starting download of {model.Name}...");

                // Create the model directory if it doesn't exist
                var modelDir = Path.Combine(_modelsBasePath, modelId);
                Directory.CreateDirectory(modelDir);

                // Determine the file extension from the URL
                var extension = Path.GetExtension(modelInfo.DownloadUrl);
                var fileName = $"{modelId}{extension}";
                var filePath = Path.Combine(modelDir, fileName);

                // Check if this model requires authentication
                bool requiresAuth = modelInfo.DownloadUrl.Contains("huggingface.co/api/models") ||
                                modelInfo.Metadata.TryGetValue("RequiresAuth", out var authValue) &&
                                authValue.Equals("true", StringComparison.OrdinalIgnoreCase);

                if (requiresAuth)
                {
                    try
                    {
                        RaiseInferenceProgress("Model requires authentication. Checking for credentials...");

                        // Get the authentication service
                        var authService = _serviceProvider.GetRequiredService<IAuthenticationService>();

                        // Use ModelRepository for authenticated download
                        var modelRepository = _serviceProvider.GetRequiredService<IModelRepository>();
                        if (modelRepository is ModelRepository repo)
                        {
                            // This will handle prompting for token if needed
                            byte[] fileData = await repo.DownloadModelWithAuthAsync(
                                modelInfo.DownloadUrl,
                                "HuggingFace",
                                model.Name,
                                authService,
                                _logger);

                            RaiseInferenceProgress($"Authentication successful. Writing file to {filePath}...");

                            // Write downloaded data to file
                            await File.WriteAllBytesAsync(filePath, fileData);

                            // Report full progress
                            progress?.Report(1.0);
                        }
                        else
                        {
                            throw new InvalidOperationException("Authenticated download required but not supported by the current implementation");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        RaiseInferenceProgress("Download canceled by user");
                        await _modelService.UpdateModelStatusAsync(modelId, ModelStatus.Error);
                        OnError?.Invoke(this, new Exception("Authentication required but download was canceled"));
                        throw;
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        RaiseInferenceProgress($"Authentication failed: {ex.Message}");
                        await _modelService.UpdateModelStatusAsync(modelId, ModelStatus.Error);
                        OnError?.Invoke(this, ex);
                        throw;
                    }
                }
                else
                {
                    // Standard download without authentication
                    await DownloadFileAsync(modelInfo.DownloadUrl, filePath, progress);
                }

                // Handle extraction if the file is compressed
                if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".tar", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".gz", StringComparison.OrdinalIgnoreCase))
                {
                    RaiseInferenceProgress("Extracting model files...");
                    await Task.Run(() => ExtractArchive(filePath, modelDir));
                }

                // Update model status and path
                RaiseInferenceProgress("Updating model information...");
                await _modelService.UpdateModelAsync(modelId, filePath, ModelStatus.Downloaded);

                RaiseInferenceProgress($"Model {model.Name} downloaded successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading model {ModelId}", modelId);
                // Update model status to error
                await _modelService.UpdateModelStatusAsync(modelId, ModelStatus.Error);
                OnError?.Invoke(this, ex);
                throw;
            }
        }

        public async Task LoadModelAsync(string modelId)
        {
            try
            {
                if (_loadedModels.ContainsKey(modelId))
                {
                    _logger.LogInformation("Model {ModelId} is already loaded", modelId);
                    return;
                }

                RaiseInferenceProgress("Loading model into memory...");

                // Get model data
                var model = await _modelService.GetModelAsync(modelId);
                if (model == null)
                {
                    throw new KeyNotFoundException($"Model {modelId} not found");
                }

                if (model.Status != ModelStatus.Downloaded || string.IsNullOrEmpty(model.LocalPath))
                {
                    throw new InvalidOperationException($"Model {modelId} is not downloaded or path is invalid");
                }

                // Create ONNX session options
                var sessionOptions = new SessionOptions();

                // Check if we should use GPU acceleration
                var settings = await _modelService.GetUserSettingsAsync();
                if (settings.UseGPU)
                {
                    // Try to use CUDA/DirectML
                    try
                    {
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            // On Windows, try DirectML
                            sessionOptions.AppendExecutionProvider_DML();
                            _logger.LogInformation("Using DirectML for GPU acceleration");
                        }
                        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                        {
                            // On Linux, try CUDA
                            sessionOptions.AppendExecutionProvider_CUDA();
                            _logger.LogInformation("Using CUDA for GPU acceleration");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to initialize GPU acceleration, falling back to CPU");
                    }
                }

                // Load model using ONNX Runtime
                _logger.LogInformation("Creating inference session for model {ModelId} from {Path}", modelId, model.LocalPath);

                // This is a simulated load to avoid actual ONNX dependencies
                // In a real implementation, we would create an InferenceSession
                await Task.Delay(1000); // Simulate loading time

                // Create a dummy session for demonstration 
                var session = new InferenceSession(model.LocalPath, sessionOptions);
                _loadedModels[modelId] = session;

                RaiseInferenceProgress($"Model {model.Name} loaded successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading model {ModelId}", modelId);
                OnError?.Invoke(this, ex);
                throw;
            }
        }

        public async Task UnloadModelAsync(string modelId)
        {
            try
            {
                if (_loadedModels.TryGetValue(modelId, out var session))
                {
                    _logger.LogInformation("Unloading model {ModelId}", modelId);
                    session.Dispose();
                    _loadedModels.Remove(modelId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unloading model {ModelId}", modelId);
                OnError?.Invoke(this, ex);
                throw;
            }
        }

        private async Task DownloadFileAsync(string url, string destinationPath, IProgress<double>? progress = null)
        {
            try
            {
                // Ensure the destination directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));

                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

                // Check for specific errors
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger.LogError("Authentication required for {Url}. Using a different model source is recommended.", url);
                    throw new UnauthorizedAccessException($"Authentication required to download model from {url}. Please try a different model.");
                }

                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1L && progress != null;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                var totalBytesRead = 0L;
                var bytesRead = 0;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;

                    if (canReportProgress)
                    {
                        var progressValue = (double)totalBytesRead / totalBytes;
                        progress!.Report(progressValue);

                        if (totalBytesRead % (1024 * 1024) == 0) // Log every MB
                        {
                            RaiseInferenceProgress($"Downloaded {totalBytesRead / (1024 * 1024)} MB of {totalBytes / (1024 * 1024)} MB");
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Authentication error downloading file from {Url} to {Path}", url, destinationPath);
                throw; // Rethrow the specific exception
            }
            catch (HttpRequestException ex)
            {
                if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogError("Model file not found at {Url}. The file may have been moved or deleted.", url);
                    throw new FileNotFoundException($"Model file not found at {url}. Please try a different model.", destinationPath);
                }

                _logger.LogError(ex, "Error downloading file from {Url} to {Path}", url, destinationPath);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading file from {Url} to {Path}", url, destinationPath);
                throw;
            }
        }

        private void ExtractArchive(string archivePath, string destinationPath)
        {
            try
            {
                using var archive = ArchiveFactory.Open(archivePath);
                foreach (var entry in archive.Entries)
                {
                    if (!entry.IsDirectory)
                    {
                        _logger.LogInformation("Extracting {Entry}", entry.Key);
                        entry.WriteToDirectory(destinationPath, new ExtractionOptions
                        {
                            ExtractFullPath = true,
                            Overwrite = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting archive {Path}", archivePath);
                throw;
            }
        }

        private string FormatConversationHistory(IEnumerable<(bool IsUser, string Message)> history)
        {
            var formattedHistory = new System.Text.StringBuilder();
            foreach (var (isUser, message) in history)
            {
                formattedHistory.AppendLine(isUser ? $"User: {message}" : $"Assistant: {message}");
            }
            return formattedHistory.ToString().Trim();
        }

        private void RaiseInferenceProgress(string message)
        {
            _logger.LogInformation(message);
            OnInferenceProgress?.Invoke(this, message);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                foreach (var session in _loadedModels.Values)
                {
                    session.Dispose();
                }
                _loadedModels.Clear();
                _httpClient.Dispose();
                _disposed = true;
            }
        }
    }
}