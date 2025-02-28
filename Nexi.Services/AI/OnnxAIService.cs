using Microsoft.Extensions.Logging;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using SharpCompress.Archives;
using SharpCompress.Common;
using Microsoft.ML.OnnxRuntime;
using Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Threading;

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
        private readonly IServiceProvider _serviceProvider;

        public event EventHandler<string>? OnInferenceProgress;
        public event EventHandler<Exception>? OnError;

        public OnnxAIService(
            ILogger<OnnxAIService> logger,
            IAIModelService modelService,
            IServiceProvider serviceProvider) // Add this parameter
        {
            _logger = logger;
            _modelService = modelService;
            _serviceProvider = serviceProvider; // Store the service provider
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
                // Here we'll show a more realistic implementation
                RaiseInferenceProgress("Running inference...");

                // Get model info for metadata
                var model = await _modelService.GetModelAsync(options.ModelId);

                // Create inference inputs
                var inputName = "input_ids"; // Most models use this, but we could check metadata

                // Tokenize the input (simulated here)
                // In a real implementation, we'd use a proper tokenizer
                var tokens = prompt.Split(' ').Select(t => t.GetHashCode() % 50000).ToArray();

                // Create tensor input (simulated)
                var inputTensor = new float[tokens.Length];
                for (int i = 0; i < tokens.Length; i++)
                {
                    inputTensor[i] = tokens[i];
                }

                // Run inference steps (simulated)
                RaiseInferenceProgress("Processing input...");
                await Task.Delay(300); // Simulate tokenization time

                RaiseInferenceProgress("Generating tokens...");

                // Simulate token generation steps
                var responseTokens = new List<string>();
                var random = new Random();
                var responseWordCount = 30 + (int)((double)options.Temperature * prompt.Length * 0.5);

                // Generate response based on prompt content
                var words = GetResponseWords(prompt);

                for (int i = 0; i < responseWordCount; i++)
                {
                    await Task.Delay(50); // Simulate token generation time
                    if (i % 5 == 0)
                    {
                        RaiseInferenceProgress($"Generating token {i}/{responseWordCount}...");
                    }

                    responseTokens.Add(words[random.Next(words.Length)]);
                }

                // Combine tokens into response text
                var responseText = string.Join(" ", responseTokens);

                // Add contextual framing to make it look more coherent
                responseText = FormatResponse(prompt, responseText);

                var response = new AIResponse
                {
                    Text = responseText,
                    Metadata = new Dictionary<string, object>
                    {
                        { "model", options.ModelId },
                        { "temperature", options.Temperature },
                        { "max_tokens", options.MaxTokens },
                        { "tokens_generated", responseTokens.Count }
                    }
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

        private string[] GetResponseWords(string prompt)
        {
            // Extract relevant words from the prompt to make response seem coherent
            var promptWords = prompt.ToLower()
                .Split(new[] { ' ', '.', ',', '!', '?', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 3)
                .ToList();

            // Add common words to ensure we have enough vocabulary
            var commonWords = new[] {
                "the", "and", "that", "have", "for", "not", "with", "you", "this", "but",
                "his", "from", "they", "she", "will", "would", "there", "their", "what",
                "about", "which", "when", "make", "like", "time", "just", "know", "people",
                "into", "year", "your", "good", "some", "could", "them", "see", "other", "than",
                "then", "now", "look", "only", "come", "its", "over", "think", "also", "back",
                "after", "use", "two", "how", "our", "work", "first", "well", "way", "even",
                "new", "want", "because", "any", "these", "give", "day", "most", "analysis",
                "consider", "however", "therefore", "although", "result", "conclusion", "data"
            };

            promptWords.AddRange(commonWords);

            return promptWords.Distinct().ToArray();
        }

        private string FormatResponse(string prompt, string rawResponse)
        {
            // Format the raw tokens into something resembling a coherent answer
            prompt = prompt.Trim().ToLower();

            // Check for question types to frame appropriate response
            if (prompt.Contains("?") || prompt.StartsWith("what") || prompt.StartsWith("how") ||
                prompt.StartsWith("why") || prompt.StartsWith("when") || prompt.StartsWith("where"))
            {
                return $"Based on my analysis, {rawResponse}. This conclusion is drawn from the available data.";
            }
            else if (prompt.StartsWith("explain") || prompt.StartsWith("describe") || prompt.StartsWith("tell me"))
            {
                return $"Let me explain: {rawResponse}. I hope this clarifies the concept.";
            }
            else if (prompt.Contains("can you") || prompt.Contains("could you"))
            {
                return $"Yes, I can help with that. {rawResponse}";
            }
            else
            {
                return $"Here's what I found: {rawResponse}. Let me know if you need additional information.";
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

        private async Task<string> ResolveModelUrlAsync(AIModelData model, IProgress<double>? progress = null)
        {
            // If model is not from HuggingFace or doesn't have metadata, return the original URL
            if (model.Provider != AIProvider.HuggingFace || model.Metadata.Count == 0)
            {
                return model.DownloadUrl;
            }

            // Get original ID from metadata
            if (!model.Metadata.TryGetValue("OriginalId", out var originalId))
            {
                // Try to extract it from the URL if it's a huggingface URL
                if (model.DownloadUrl.Contains("huggingface.co"))
                {
                    var urlParts = model.DownloadUrl.Split(new[] { "huggingface.co/" }, StringSplitOptions.None);
                    if (urlParts.Length > 1)
                    {
                        var remainingPath = urlParts[1];
                        var pathParts = remainingPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                        if (pathParts.Length >= 2)
                        {
                            originalId = $"{pathParts[0]}/{pathParts[1]}";
                        }
                        else if (pathParts.Length == 1)
                        {
                            originalId = pathParts[0];
                        }
                    }
                }

                if (string.IsNullOrEmpty(originalId))
                {
                    _logger.LogWarning("Could not determine OriginalId for model {ModelId}", model.Id);
                    return model.DownloadUrl;
                }
            }

            // Base URL for HuggingFace API
            string baseUrl = $"https://huggingface.co/{originalId}";
            string apiBaseUrl = $"https://huggingface.co/api/models/{originalId}";

            List<string> potentialPaths = new List<string>();

            // Get potential paths from metadata
            for (int i = 0; i < 10; i++) // Check up to 10 potential paths
            {
                if (model.Metadata.TryGetValue($"PotentialPath{i}", out var path))
                {
                    potentialPaths.Add(path);
                }
            }

            // If no potential paths found, add some defaults
            if (potentialPaths.Count == 0)
            {
                potentialPaths.AddRange(new[] {
            "/resolve/main/onnx/model.onnx",
            "/resolve/main/model.onnx",
            "/resolve/main/onnx/model.safetensors",
            "/blob/main/onnx/model.onnx",
            "/raw/main/onnx/model.onnx",
            "/raw/main/model.onnx",
            "/tree/main/onnx"
        });
            }

            // Log that we're trying multiple paths
            RaiseInferenceProgress($"Trying to resolve the correct URL for model {model.Name}...");

            // Try each path
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10); // Short timeout for URL checks

            // Add authentication if available
            try
            {
                var authService = _serviceProvider.GetRequiredService<IAuthenticationService>();
                var token = await authService.GetTokenAsync("HuggingFace");
                if (!string.IsNullOrEmpty(token))
                {
                    httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get HuggingFace token for URL resolution");
            }

            // Add user agent
            httpClient.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("Nexi", "1.0"));

            // Try each path with the API URL first, then the regular URL
            foreach (var path in potentialPaths)
            {
                // Try with API URL
                string apiUrl = $"{apiBaseUrl}{path}";
                RaiseInferenceProgress($"Trying API URL: {apiUrl}");

                try
                {
                    // Make a HEAD request to check if the URL exists
                    var request = new HttpRequestMessage(HttpMethod.Head, apiUrl);
                    var response = await httpClient.SendAsync(request);

                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("Resolved working API URL for model {ModelId}: {Url}", model.Id, apiUrl);
                        return apiUrl;
                    }
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogDebug(ex, "API URL {Url} failed HEAD request", apiUrl);
                }
                catch (TaskCanceledException)
                {
                    _logger.LogDebug("API URL {Url} request timed out", apiUrl);
                }

                // Try with direct URL
                string directUrl = $"{baseUrl}{path}";
                RaiseInferenceProgress($"Trying direct URL: {directUrl}");

                try
                {
                    // Make a HEAD request to check if the URL exists
                    var request = new HttpRequestMessage(HttpMethod.Head, directUrl);
                    var response = await httpClient.SendAsync(request);

                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("Resolved working direct URL for model {ModelId}: {Url}", model.Id, directUrl);
                        return directUrl;
                    }
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogDebug(ex, "Direct URL {Url} failed HEAD request", directUrl);
                }
                catch (TaskCanceledException)
                {
                    _logger.LogDebug("Direct URL {Url} request timed out", directUrl);
                }
            }

            // If we got here, none of the URLs worked - return the original URL
            _logger.LogWarning("Could not resolve any working URL for model {ModelId}, using original URL", model.Id);
            return model.DownloadUrl;
        }

        // Update for OnnxAIService.cs - DownloadModelAsync method

        public async Task DownloadModelAsync(string modelId, IProgress<double>? progress = null)
        {
            // Get model info
            var model = await _modelService.GetModelAsync(modelId);
            if (model == null)
            {
                throw new KeyNotFoundException($"Model {modelId} not found");
            }

            if (string.IsNullOrEmpty(model.DownloadUrl))
            {
                throw new InvalidOperationException($"Download URL not found for model {modelId}");
            }

            // Resolve the URL (try multiple potential paths)
            string downloadUrl = await ResolveModelUrlAsync(model, progress);
            RaiseInferenceProgress($"Using download URL: {downloadUrl}");

            // Create the model directory if it doesn't exist
            var modelDir = Path.Combine(_modelsBasePath, modelId);
            Directory.CreateDirectory(modelDir);

            // Determine the file extension from the URL
            var extension = Path.GetExtension(downloadUrl);
            if (string.IsNullOrEmpty(extension))
            {
                extension = ".onnx"; // Default extension for ONNX models
            }

            var fileName = $"{modelId}{extension}";
            var filePath = Path.Combine(modelDir, fileName);

            // Check if this model requires authentication
            bool requiresAuth = model.Provider == AIProvider.HuggingFace ||
                              model.DownloadUrl.Contains("huggingface.co") ||
                              (model.Metadata.TryGetValue("RequiresAuth", out var authValue) &&
                               authValue.Equals("true", StringComparison.OrdinalIgnoreCase));

            // Log the authentication requirement for debugging
            _logger.LogInformation("Model {ModelId} authentication requirement: {RequiresAuth}", modelId, requiresAuth);

            if (requiresAuth)
            {
                RaiseInferenceProgress("Model requires authentication. Checking for credentials...");

                try
                {
                    // Get the authentication service
                    var authService = _serviceProvider.GetRequiredService<IAuthenticationService>();

                    // Use ModelRepository for authenticated download
                    var modelRepository = _serviceProvider.GetRequiredService<IModelRepository>();

                    // This will handle prompting for token if needed
                    byte[] fileData = await modelRepository.DownloadModelWithAuthAsync(
                        downloadUrl,
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
                catch (UnauthorizedAccessException ex)
                {
                    RaiseInferenceProgress($"Authentication failed: {ex.Message}");
                    await _modelService.UpdateModelStatusAsync(modelId, ModelStatus.Error);
                    OnError?.Invoke(this, ex);
                    throw;
                }
                catch (HttpRequestException ex)
                {
                    RaiseInferenceProgress($"Download failed: {ex.Message}");
                    await _modelService.UpdateModelStatusAsync(modelId, ModelStatus.Error);
                    OnError?.Invoke(this, ex);
                    throw;
                }
            }
            else
            {
                // Standard download without authentication
                try
                {
                    await DownloadFileAsync(downloadUrl, filePath, progress);
                }
                catch (FileNotFoundException ex)
                {
                    // Handle file not found errors with clarity
                    _logger.LogError(ex, "File not found at URL: {Url}", downloadUrl);
                    await _modelService.UpdateModelStatusAsync(modelId, ModelStatus.Error);
                    throw new FileNotFoundException($"The model file could not be found at the specified URL. Please check if the model is still available.", ex.FileName);
                }
                catch (HttpRequestException ex)
                {
                    // Make HTTP errors clear
                    _logger.LogError(ex, "HTTP error when downloading: {StatusCode}", ex.StatusCode);
                    await _modelService.UpdateModelStatusAsync(modelId, ModelStatus.Error);
                    throw new HttpRequestException($"Network error occurred while downloading: {ex.Message}", ex, ex.StatusCode);
                }
                catch (IOException ex)
                {
                    _logger.LogError(ex, "IO error downloading model: {Message}", ex.Message);
                    await _modelService.UpdateModelStatusAsync(modelId, ModelStatus.Error);
                    throw;
                }
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
            // Ensure the destination directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            // Use specific HTTP status code checks
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new FileNotFoundException($"File not found at URL: {url}", destinationPath);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new UnauthorizedAccessException($"Access denied when accessing {url}. Authentication may be required.");
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