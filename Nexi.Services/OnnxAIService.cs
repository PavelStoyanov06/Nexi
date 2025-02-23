using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;
using SharpCompress.Common;
using SharpCompress.Readers;
using System.Text;

namespace Nexi.Services.AI
{
    public class OnnxAIService : IAIService, IDisposable
    {
        private readonly ILogger<OnnxAIService> _logger;
        private readonly Dictionary<string, InferenceSession> _sessions;
        private readonly HttpClient _httpClient;
        private readonly string _modelsDirectory;
        private AIRequestOptions _currentSettings;
        private bool _disposed;

        public event EventHandler<string>? OnInferenceProgress;
        public event EventHandler<Exception>? OnError;

        public OnnxAIService(ILogger<OnnxAIService> logger)
        {
            _logger = logger;
            _sessions = new Dictionary<string, InferenceSession>();
            _httpClient = new HttpClient();
            _modelsDirectory = Path.Combine(AppContext.BaseDirectory, "Models");
            _currentSettings = new AIRequestOptions();

            // Ensure models directory exists
            Directory.CreateDirectory(_modelsDirectory);
        }

        public async Task<AIResponse> GetCompletionAsync(string prompt, AIRequestOptions? options = null)
        {
            options ??= _currentSettings;

            try
            {
                var modelId = options.ModelId;
                if (!_sessions.ContainsKey(modelId))
                {
                    throw new InvalidOperationException($"Model {modelId} is not loaded.");
                }

                var session = _sessions[modelId];

                // Create input tensor
                var inputTensor = CreateInputTensor(prompt);

                // Run inference
                var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input_ids", inputTensor) };
                using var results = session.Run(inputs);

                // Process results
                var output = ProcessOutput(results);

                return new AIResponse
                {
                    Text = output,
                    Metadata = new Dictionary<string, object>
                    {
                        ["model_id"] = modelId,
                        ["timestamp"] = DateTime.UtcNow
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during inference");
                OnError?.Invoke(this, ex);
                throw;
            }
        }

        public async Task<AIResponse> GetCompletionWithHistoryAsync(
            IEnumerable<(bool isUser, string message)> history,
            string prompt,
            AIRequestOptions? options = null)
        {
            // Combine history and prompt
            var fullPrompt = CombineHistoryWithPrompt(history, prompt);
            return await GetCompletionAsync(fullPrompt, options);
        }

        public async Task<float[]> GetEmbeddingsAsync(string text)
        {
            // Implementation for embeddings will come later
            throw new NotImplementedException();
        }

        public async Task LoadModelAsync(string modelId)
        {
            try
            {
                if (_sessions.ContainsKey(modelId))
                {
                    _logger.LogInformation("Model {ModelId} is already loaded", modelId);
                    return;
                }

                var modelPath = Path.Combine(_modelsDirectory, modelId, "model.onnx");
                if (!File.Exists(modelPath))
                {
                    throw new FileNotFoundException($"Model file not found at {modelPath}");
                }

                // Create session options
                var sessionOptions = new SessionOptions();
                sessionOptions.RegisterCustomOpLibrary("DirectML.dll"); // For GPU support

                // Create inference session
                var session = new InferenceSession(modelPath, sessionOptions);
                _sessions[modelId] = session;

                _logger.LogInformation("Model {ModelId} loaded successfully", modelId);
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
            if (_sessions.TryGetValue(modelId, out var session))
            {
                session.Dispose();
                _sessions.Remove(modelId);
                _logger.LogInformation("Model {ModelId} unloaded", modelId);
            }
        }

        public bool IsModelLoaded(string modelId)
        {
            return _sessions.ContainsKey(modelId);
        }

        public async Task UpdateSettingsAsync(AIRequestOptions options)
        {
            _currentSettings = options;
        }

        public AIRequestOptions GetCurrentSettings()
        {
            return _currentSettings;
        }

        public async Task DownloadModelAsync(string modelId, string downloadUrl, IProgress<double>? progress = null)
        {
            try
            {
                var modelDir = Path.Combine(_modelsDirectory, modelId);
                Directory.CreateDirectory(modelDir);

                // Download model file
                var tempFile = Path.GetTempFileName();
                using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1;
                    using var contentStream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = File.Create(tempFile);

                    var buffer = new byte[8192];
                    var totalBytesRead = 0L;
                    var bytesRead = 0;

                    while ((bytesRead = await contentStream.ReadAsync(buffer)) != 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                        totalBytesRead += bytesRead;

                        if (totalBytes != -1 && progress != null)
                        {
                            var percentage = (double)totalBytesRead / totalBytes;
                            progress.Report(percentage);
                        }
                    }
                }

                // Extract if it's a compressed file
                if (IsCompressedFile(tempFile))
                {
                    await ExtractModelAsync(tempFile, modelDir, progress);
                }
                else
                {
                    var modelPath = Path.Combine(modelDir, "model.onnx");
                    File.Move(tempFile, modelPath, true);
                }

                _logger.LogInformation("Model {ModelId} downloaded successfully", modelId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading model {ModelId}", modelId);
                OnError?.Invoke(this, ex);
                throw;
            }
        }

        private bool IsCompressedFile(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension switch
            {
                ".gz" or ".zip" or ".7z" or ".tar" => true,
                _ => false
            };
        }

        private async Task ExtractModelAsync(string archivePath, string destinationPath, IProgress<double>? progress)
        {
            try
            {
                using var stream = File.OpenRead(archivePath);
                using var reader = ReaderFactory.Open(stream);

                while (reader.MoveToNextEntry())
                {
                    if (!reader.Entry.IsDirectory)
                    {
                        reader.WriteEntryToDirectory(destinationPath, new ExtractionOptions
                        {
                            ExtractFullPath = true,
                            Overwrite = true
                        });
                    }
                }
            }
            finally
            {
                if (File.Exists(archivePath))
                {
                    File.Delete(archivePath);
                }
            }
        }

        private string CombineHistoryWithPrompt(IEnumerable<(bool isUser, string message)> history, string prompt)
        {
            var combined = new StringBuilder();
            foreach (var (isUser, message) in history)
            {
                combined.AppendLine(isUser ? $"User: {message}" : $"Assistant: {message}");
            }
            combined.AppendLine($"User: {prompt}");
            return combined.ToString();
        }

        private DenseTensor<float> CreateInputTensor(string input)
        {
            // This will need to be implemented based on the specific tokenizer/model requirements
            throw new NotImplementedException();
        }

        private string ProcessOutput(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results)
        {
            // This will need to be implemented based on the specific model output format
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                foreach (var session in _sessions.Values)
                {
                    session.Dispose();
                }
                _sessions.Clear();
                _httpClient.Dispose();
                _disposed = true;
            }
        }
    }
}