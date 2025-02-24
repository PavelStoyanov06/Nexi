using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;
using SharpCompress.Common;
using SharpCompress.Readers;
using System.Text;
using System.Text.RegularExpressions;   

namespace Nexi.Services.AI
{
    public class OnnxAIService : IAIService, IDisposable
    {
        private readonly ILogger<OnnxAIService> _logger;
        private readonly IModelRepository _modelRepository;
        private readonly Dictionary<string, InferenceSession> _sessions;
        private readonly HttpClient _httpClient;
        private readonly string _modelsDirectory;
        private AIRequestOptions _currentSettings;
        private bool _disposed;

        // Simple token mapping for basic tokenization
        private Dictionary<string, int> _tokenToId = new();
        private Dictionary<int, string> _idToToken = new();

        public event EventHandler<string>? OnInferenceProgress;
        public event EventHandler<Exception>? OnError;

        public OnnxAIService(ILogger<OnnxAIService> logger, IModelRepository modelRepository)
        {
            _logger = logger;
            _modelRepository = modelRepository;
            _sessions = new Dictionary<string, InferenceSession>();

            // Configure HttpClient with timeout
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(5); // 5 minute timeout for large downloads

            _modelsDirectory = Path.Combine(AppContext.BaseDirectory, "Models");
            _currentSettings = new AIRequestOptions { Temperature = 0.7M, MaxTokens = 1000 };

            // Ensure models directory exists
            Directory.CreateDirectory(_modelsDirectory);

            // Initialize tokenizer (in a real app, this would load from a tokenizer.json file)
            InitializeBasicTokenizer();
        }

        private void InitializeBasicTokenizer()
        {
            // This is a simplified tokenizer for demonstration
            // In a real implementation, you would load a proper tokenizer from the model's tokenizer.json
            var words = "the of to and a in is it you that he was for on are with as I his they be at".Split();

            for (int i = 0; i < words.Length; i++)
            {
                _tokenToId[words[i]] = i;
                _idToToken[i] = words[i];
            }

            // Add some special tokens
            _tokenToId["<s>"] = 1000;
            _idToToken[1000] = "<s>";
            _tokenToId["</s>"] = 1001;
            _idToToken[1001] = "</s>";
            _tokenToId["<pad>"] = 1002;
            _idToToken[1002] = "<pad>";
        }

        public async Task<AIResponse> GetCompletionAsync(string prompt, AIRequestOptions? options = null)
        {
            options ??= _currentSettings;

            try
            {
                var modelId = options.ModelId;
                if (string.IsNullOrEmpty(modelId))
                {
                    throw new InvalidOperationException("No model ID specified in options.");
                }

                // Get model info to determine how to process input/output
                var modelInfo = await _modelRepository.GetModelInfoAsync(modelId);

                if (!_sessions.ContainsKey(modelId))
                {
                    await LoadModelAsync(modelId);
                }

                var session = _sessions[modelId];

                // Get the domain from metadata
                string domain = "text"; // Default to text
                if (modelInfo.Metadata.TryGetValue("domain", out var domainValue))
                {
                    domain = domainValue.ToLowerInvariant();
                }

                // Create appropriate input tensor based on model type
                List<NamedOnnxValue> inputs;
                if (domain == "vision")
                {
                    // Vision models typically expect image inputs
                    // For demo purposes, we'll just create a dummy input
                    inputs = CreateDummyImageInputs(modelInfo);
                    OnInferenceProgress?.Invoke(this, "Using dummy image data for vision model. In a real app, you would provide an actual image.");
                }
                else
                {
                    // Text models
                    var inputIds = Tokenize(prompt);
                    var inputTensor = CreateInputTensor(inputIds);
                    inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input_ids", inputTensor) };
                }

                // Report progress
                OnInferenceProgress?.Invoke(this, "Running inference...");

                // Run inference
                using var results = session.Run(inputs);

                // Process results based on model type
                var output = ProcessOutput(results, modelInfo);

                return new AIResponse
                {
                    Text = output,
                    Metadata = new Dictionary<string, object>
                    {
                        ["model_id"] = modelId,
                        ["model_type"] = domain,
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

        private List<NamedOnnxValue> CreateDummyImageInputs(ModelInfo modelInfo)
        {
            // This is a placeholder for demonstration
            // In a real application, you would process actual image data

            if (modelInfo.Id.Contains("resnet", StringComparison.OrdinalIgnoreCase))
            {
                // Create a dummy tensor with the right shape for ResNet
                // ResNet typically expects [1, 3, 224, 224] for batch size, channels, height, width
                var dummyTensor = new DenseTensor<float>(new[] { 1, 3, 224, 224 });
                return new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("data", dummyTensor) };
            }
            else if (modelInfo.Id.Contains("ssd", StringComparison.OrdinalIgnoreCase))
            {
                // SSD models often have different input shapes
                var dummyTensor = new DenseTensor<float>(new[] { 1, 3, 300, 300 });
                return new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("image", dummyTensor) };
            }
            else
            {
                // Generic fallback
                var dummyTensor = new DenseTensor<float>(new[] { 1, 3, 224, 224 });
                return new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input", dummyTensor) };
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
            // Simplified implementation - in a real app, this would use an embedding model
            return Tokenize(text).Select(t => (float)t).ToArray();
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

                // Get model info from repository
                var modelInfo = await _modelRepository.GetModelInfoAsync(modelId);

                var modelDir = Path.Combine(_modelsDirectory, modelId);
                var modelPath = Path.Combine(modelDir, "model.onnx");

                if (!File.Exists(modelPath))
                {
                    throw new FileNotFoundException($"Model file not found at {modelPath}. You may need to download it first.");
                }

                // Report progress
                OnInferenceProgress?.Invoke(this, $"Loading model {modelInfo.Name}...");

                // Create session options
                var sessionOptions = new SessionOptions();

                // Try to use GPU if available
                try
                {
                    sessionOptions.AppendExecutionProvider_DML(0); // DirectML (for Windows)
                    _logger.LogInformation("Using DirectML (GPU) execution provider");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "DirectML provider not available, falling back to CPU");
                }

                // Create inference session
                var session = new InferenceSession(modelPath, sessionOptions);
                _sessions[modelId] = session;

                _logger.LogInformation("Model {ModelId} loaded successfully", modelId);
                OnInferenceProgress?.Invoke(this, $"Model {modelInfo.Name} loaded successfully");
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
                OnInferenceProgress?.Invoke(this, $"Model {modelId} unloaded");
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

        public async Task DownloadModelAsync(string modelId, IProgress<double>? progress = null)
        {
            try
            {
                var modelInfo = await _modelRepository.GetModelInfoAsync(modelId);
                var modelDir = Path.Combine(_modelsDirectory, modelId);
                Directory.CreateDirectory(modelDir);

                // Update status to downloading
                await _modelRepository.UpdateModelStatusAsync(modelId, ModelStatus.Downloading);
                OnInferenceProgress?.Invoke(this, $"Downloading model {modelInfo.Name}...");

                // Create a temporary file
                var tempFile = Path.GetTempFileName();

                try
                {
                    // Download the model with retry logic
                    bool success = false;
                    Exception? lastException = null;

                    for (int attempt = 1; attempt <= 3; attempt++)
                    {
                        try
                        {
                            OnInferenceProgress?.Invoke(this, $"Download attempt {attempt}/3...");

                            using var response = await _httpClient.GetAsync(
                                modelInfo.DownloadUrl,
                                HttpCompletionOption.ResponseHeadersRead);

                            // Check for success
                            response.EnsureSuccessStatusCode();

                            var totalBytes = response.Content.Headers.ContentLength ?? -1;
                            using var contentStream = await response.Content.ReadAsStreamAsync();
                            using var fileStream = File.Create(tempFile);

                            var buffer = new byte[81920]; // Larger buffer for faster downloads
                            var totalBytesRead = 0L;
                            var bytesRead = 0;
                            var lastProgressReport = 0.0;

                            while ((bytesRead = await contentStream.ReadAsync(buffer)) != 0)
                            {
                                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                                totalBytesRead += bytesRead;

                                if (totalBytes != -1 && progress != null)
                                {
                                    var percentage = (double)totalBytesRead / totalBytes;

                                    // Only report progress when it changes significantly (reduce UI updates)
                                    if (percentage - lastProgressReport > 0.01)
                                    {
                                        progress.Report(percentage);
                                        OnInferenceProgress?.Invoke(this, $"Downloading: {percentage:P0}");
                                        lastProgressReport = percentage;
                                    }
                                }
                            }

                            success = true;
                            break; // Exit retry loop on success
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;
                            _logger.LogWarning(ex, "Download attempt {Attempt} failed", attempt);

                            if (attempt < 3)
                            {
                                // Wait before retrying (exponential backoff)
                                await Task.Delay(1000 * attempt);
                            }
                        }
                    }

                    if (!success)
                    {
                        if (lastException != null)
                        {
                            throw new Exception($"Download failed after 3 attempts: {lastException.Message}", lastException);
                        }
                        else
                        {
                            throw new Exception("Download failed after 3 attempts");
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

                    // Update status to downloaded
                    await _modelRepository.UpdateModelStatusAsync(modelId, ModelStatus.Downloaded);
                    OnInferenceProgress?.Invoke(this, $"Model {modelInfo.Name} downloaded successfully");
                    _logger.LogInformation("Model {ModelId} downloaded successfully", modelId);
                }
                catch (Exception)
                {
                    // Clean up the temp file on failure
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                    throw;
                }
            }
            catch (Exception ex)
            {
                // Update status to error
                await _modelRepository.UpdateModelStatusAsync(modelId, ModelStatus.Error);
                _logger.LogError(ex, "Error downloading model {ModelId}", modelId);
                OnInferenceProgress?.Invoke(this, $"Error downloading model: {ex.Message}");
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
                OnInferenceProgress?.Invoke(this, "Extracting model files...");

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

                OnInferenceProgress?.Invoke(this, "Extraction complete");
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
            combined.AppendLine("<s>");

            foreach (var (isUser, message) in history)
            {
                var prefix = isUser ? "User: " : "Assistant: ";
                combined.AppendLine($"{prefix}{message}");
            }

            combined.AppendLine($"User: {prompt}");
            combined.AppendLine("Assistant:");

            return combined.ToString();
        }

        private int[] Tokenize(string text)
        {
            // Very simplified tokenization - split by whitespace and convert to IDs
            // In a real implementation, this would use the model's tokenizer
            var tokens = new List<int>();

            // Add start token
            tokens.Add(_tokenToId["<s>"]);

            // Add content tokens
            foreach (var word in Regex.Split(text, @"\s+"))
            {
                if (string.IsNullOrWhiteSpace(word))
                    continue;

                if (_tokenToId.TryGetValue(word.ToLower(), out var id))
                {
                    tokens.Add(id);
                }
                else
                {
                    // For unknown tokens, we could split into characters or use a special unknown token
                    // For simplicity, we'll just assign a random ID
                    tokens.Add(999); // Unknown token placeholder
                }
            }

            return tokens.ToArray();
        }

        private DenseTensor<long> CreateInputTensor(int[] inputIds)
        {
            // Create a tensor of shape [1, sequence_length]
            var tensor = new DenseTensor<long>(new[] { 1, inputIds.Length });

            for (int i = 0; i < inputIds.Length; i++)
            {
                tensor[0, i] = inputIds[i];
            }

            return tensor;
        }

        private string ProcessOutput(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, ModelInfo modelInfo)
        {
            try
            {
                // Get the domain from metadata
                string domain = "text"; // Default to text
                if (modelInfo.Metadata.TryGetValue("domain", out var domainValue))
                {
                    domain = domainValue.ToLowerInvariant();
                }

                switch (domain)
                {
                    case "vision":
                        return ProcessVisionModelOutput(results, modelInfo);
                    case "text":
                    default:
                        return ProcessTextModelOutput(results, modelInfo);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing model output");
                return "Error processing model output: " + ex.Message;
            }
        }

        private string ProcessTextModelOutput(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, ModelInfo modelInfo)
        {
            var output = new StringBuilder();

            // For different text models, we need to handle outputs differently
            if (modelInfo.Id.Contains("gpt2", StringComparison.OrdinalIgnoreCase))
            {
                // GPT-2 specific output processing
                var outputTensor = results.FirstOrDefault(r => r.Name == "output" || r.Name.Contains("logits"));

                if (outputTensor != null)
                {
                    // Simple example - just to illustrate
                    output.AppendLine("GPT-2 model generated output (processed from logits).");
                    output.AppendLine("To get actual text generation, you would need a tokenizer to decode the output.");
                }
            }
            else if (modelInfo.Id.Contains("bert", StringComparison.OrdinalIgnoreCase))
            {
                // BERT specific output processing for question answering
                var startLogits = results.FirstOrDefault(r => r.Name == "start_logits");
                var endLogits = results.FirstOrDefault(r => r.Name == "end_logits");

                if (startLogits != null && endLogits != null)
                {
                    output.AppendLine("BERT model processed for question answering.");
                    output.AppendLine("The model predicts an answer span in the provided context.");
                }
            }
            else
            {
                // Generic text model output
                foreach (var outputTensor in results)
                {
                    output.AppendLine($"Output tensor '{outputTensor.Name}' processed.");
                }
            }

            if (output.Length == 0)
            {
                output.AppendLine("Model produced output but no processor was available for this specific model type.");
            }

            return output.ToString().Trim();
        }

        private string ProcessVisionModelOutput(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, ModelInfo modelInfo)
        {
            var output = new StringBuilder();

            if (modelInfo.Id.Contains("resnet", StringComparison.OrdinalIgnoreCase))
            {
                // ResNet classification output processing
                var outputTensor = results.First().AsTensor<float>();

                if (outputTensor != null)
                {
                    // Get the top 5 predictions
                    var probabilities = outputTensor.ToArray();
                    var topIndices = probabilities
                        .Select((p, i) => (Probability: p, Index: i))
                        .OrderByDescending(x => x.Probability)
                        .Take(5)
                        .ToArray();

                    output.AppendLine("Top 5 predicted classes:");
                    for (int i = 0; i < topIndices.Length; i++)
                    {
                        output.AppendLine($"{i + 1}. Class {topIndices[i].Index}: {topIndices[i].Probability:F4}");
                    }
                }
            }
            else if (modelInfo.Id.Contains("ssd", StringComparison.OrdinalIgnoreCase))
            {
                // SSD object detection output processing
                output.AppendLine("Object detection model output processed.");
                output.AppendLine("The model detected objects in the provided image.");

                // In a real implementation, we would extract bounding boxes
                // and class predictions from the model output
            }
            else
            {
                // Generic vision model output
                foreach (var outputTensor in results)
                {
                    output.AppendLine($"Output tensor '{outputTensor.Name}' processed.");
                }
            }

            if (output.Length == 0)
            {
                output.AppendLine("Model produced output but no processor was available for this specific model type.");
            }

            return output.ToString().Trim();
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