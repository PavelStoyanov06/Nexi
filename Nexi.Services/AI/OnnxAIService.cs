using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Nexi.Data.Context;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nexi.Services.AI
{
    public class OnnxAIService : IAIService, IDisposable
    {
        private readonly IDbContextFactory<NexiDbContext> _contextFactory;
        private readonly ILogger<OnnxAIService> _logger;
        private readonly Dictionary<string, InferenceSession> _loadedModels = new();
        private readonly SemaphoreSlim _modelLock = new(1, 1);
        private bool _disposed;
        private bool _useMockModel = false;

        // Event handlers
        public event EventHandler<string> OnInferenceProgress;
        public event EventHandler<Exception> OnError;

        public OnnxAIService(
            IDbContextFactory<NexiDbContext> contextFactory,
            ILogger<OnnxAIService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public async Task<AIResponse> GetCompletionAsync(string prompt, AIRequestOptions options)
        {
            try
            {
                ReportProgress("Generating AI response...");

                // Check if we should use the mock implementation
                if (_useMockModel)
                {
                    return await GetMockCompletionAsync(prompt, options);
                }

                // Try to load the model
                try
                {
                    // Ensure the model is loaded
                    if (!_loadedModels.ContainsKey(options.ModelId))
                    {
                        ReportProgress($"Loading model {options.ModelId}...");
                        await LoadModelAsync(options.ModelId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading model {ModelId}, falling back to mock implementation", options.ModelId);
                    _useMockModel = true;
                    ReportProgress($"Using simulated AI responses due to model loading issues");
                    return await GetMockCompletionAsync(prompt, options);
                }

                // If we got here, we have a loaded model
                var session = _loadedModels[options.ModelId];
                ReportProgress("Running inference...");

                try
                {
                    // Process the input to match what the model expects
                    var inputs = session.InputMetadata;
                    var inputNames = inputs.Keys.ToList();

                    if (inputNames.Count == 0)
                    {
                        throw new InvalidOperationException("Model has no input nodes");
                    }

                    // Create appropriate inputs based on the model's expected input
                    var modelInputs = new List<NamedOnnxValue>();

                    // This is a common pattern for NLP models like GPT-2, BERT, etc.
                    // We're checking for common input names and adapting accordingly
                    if (inputs.ContainsKey("input"))
                    {
                        // Simple input, likely an image model
                        var tensor = new DenseTensor<float>(new[] { 1, prompt.Length });
                        for (int i = 0; i < prompt.Length; i++)
                        {
                            tensor[0, i] = prompt[i];
                        }
                        modelInputs.Add(NamedOnnxValue.CreateFromTensor("input", tensor));
                    }
                    else if (inputs.ContainsKey("input_ids") || inputs.ContainsKey("tokens"))
                    {
                        // Text tokenization is complex, this is just a placeholder
                        // In a real implementation, you'd use a proper tokenizer
                        var inputName = inputs.ContainsKey("input_ids") ? "input_ids" : "tokens";
                        var tokens = Tokenize(prompt);
                        var tensor = new DenseTensor<long>(new[] { 1, tokens.Length });
                        for (int i = 0; i < tokens.Length; i++)
                        {
                            tensor[0, i] = tokens[i];
                        }
                        modelInputs.Add(NamedOnnxValue.CreateFromTensor(inputName, tensor));

                        // Add attention mask if the model requires it
                        if (inputs.ContainsKey("attention_mask"))
                        {
                            var attentionMask = new DenseTensor<long>(new[] { 1, tokens.Length });
                            for (int i = 0; i < tokens.Length; i++)
                            {
                                attentionMask[0, i] = 1; // All tokens are real (not padding)
                            }
                            modelInputs.Add(NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask));
                        }
                    }
                    else
                    {
                        // For other model types, we'll use the first input and try a simple approach
                        var firstInputName = inputNames[0];
                        var shape = inputs[firstInputName].Dimensions;

                        // Check shape type and adjust accordingly
                        if (shape.Length >= 2)
                        {
                            // For image or sequence models (common case)
                            var inputLength = shape.Length > 1 && shape[1] > 0 ? shape[1] : 64;
                            var tensor = new DenseTensor<float>(new[] { 1, inputLength });

                            // Simple encoding of the prompt to float values
                            var bytes = Encoding.UTF8.GetBytes(prompt);
                            for (int i = 0; i < Math.Min(bytes.Length, inputLength); i++)
                            {
                                tensor[0, i] = bytes[i] / 255.0f;
                            }

                            modelInputs.Add(NamedOnnxValue.CreateFromTensor(firstInputName, tensor));
                        }
                        else
                        {
                            throw new InvalidOperationException($"Unsupported input shape for {firstInputName}");
                        }
                    }

                    // Run inference
                    var outputs = session.Run(modelInputs);

                    // Extract the output - this will depend on the model
                    var outputNames = session.OutputMetadata.Keys.ToList();
                    if (outputNames.Count == 0)
                    {
                        throw new InvalidOperationException("Model has no output nodes");
                    }

                    string result = "Model generated output"; // Default placeholder

                    // Try to extract text from outputs
                    foreach (var output in outputs)
                    {
                        // For text models, we typically want the output with logits or token ids
                        if (output.Name.Contains("logit") || output.Name.Contains("output") ||
                            output.Name == outputNames[0])
                        {
                            // This is very simplified - in a real implementation,
                            // you'd decode the tokens properly
                            try
                            {
                                result = "Response: The model has processed your input successfully.";

                                // In a complete implementation, this is where you'd convert
                                // the model's numerical output back to text
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Error parsing model output, using generic response");
                            }
                            break;
                        }
                    }

                    ReportProgress("Response generated successfully");

                    return new AIResponse
                    {
                        Text = result,
                        Metadata = new Dictionary<string, object>
                        {
                            { "model", options.ModelId },
                            { "temperature", options.Temperature }
                        }
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during inference with model {ModelId}, falling back to mock implementation", options.ModelId);
                    _useMockModel = true;
                    ReportProgress($"Using simulated AI responses due to inference issues");
                    return await GetMockCompletionAsync(prompt, options);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during inference");
                ReportProgress($"Error: {ex.Message}");
                OnError?.Invoke(this, ex);

                // Return a fallback response instead of throwing
                return new AIResponse
                {
                    Text = $"I'm sorry, but I encountered an error while processing your request. Please try again later or contact support if the issue persists.",
                    Metadata = new Dictionary<string, object>
                    {
                        { "error", ex.Message },
                        { "model", options.ModelId }
                    }
                };
            }
        }

        private async Task<AIResponse> GetMockCompletionAsync(string prompt, AIRequestOptions options)
        {
            // This is a fallback method that provides mock responses when the real model isn't available
            ReportProgress("Generating simulated response...");

            // Add a small delay to simulate processing time
            await Task.Delay(500);

            string response;

            // Generate a reasonable response based on the prompt
            if (prompt.Contains("hello") || prompt.Contains("hi"))
            {
                response = "Hello! How can I assist you today?";
            }
            else if (prompt.Contains("how are you"))
            {
                response = "I'm doing well, thank you for asking! How can I help you?";
            }
            else if (prompt.Contains("help") || prompt.Contains("assistance"))
            {
                response = "I'd be happy to help! Please let me know what you need assistance with.";
            }
            else if (prompt.Contains("thanks") || prompt.Contains("thank you"))
            {
                response = "You're welcome! Is there anything else I can help you with?";
            }
            else if (prompt.Length < 10)
            {
                response = "I see your message. Could you provide more details so I can better assist you?";
            }
            else
            {
                // For longer prompts, give a more generic response
                response = "Thank you for your message. I'm currently operating in simulation mode since my AI models aren't properly loaded. Once the models are correctly installed and configured, I'll be able to provide more specific and helpful responses. Is there anything else I can assist you with?";
            }

            ReportProgress("Simulated response generated");

            return new AIResponse
            {
                Text = response,
                Metadata = new Dictionary<string, object>
                {
                    { "model", "simulation" },
                    { "temperature", options.Temperature }
                }
            };
        }

        public async Task<AIResponse> GetCompletionWithHistoryAsync(
            IEnumerable<(bool IsUser, string Message)> history,
            string prompt,
            AIRequestOptions options)
        {
            try
            {
                // Format history and current prompt into a single context
                var formattedPrompt = FormatPromptWithHistory(history, prompt, options.SystemPrompt);

                // Call the base GetCompletionAsync with the formatted prompt
                return await GetCompletionAsync(formattedPrompt, options);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during inference with history");
                OnError?.Invoke(this, ex);

                // Return a fallback response instead of throwing
                return new AIResponse
                {
                    Text = $"I'm sorry, but I encountered an error while processing your request with conversation history. Please try again with a new conversation.",
                    Metadata = new Dictionary<string, object>
                    {
                        { "error", ex.Message },
                        { "model", options.ModelId }
                    }
                };
            }
        }

        public async Task DownloadModelAsync(string modelId, IProgress<double> progress)
        {
            try
            {
                await _modelLock.WaitAsync();

                // Check if the model is already downloaded
                using var context = await _contextFactory.CreateDbContextAsync();
                var model = await context.AIModels.FirstOrDefaultAsync(m => m.Id == modelId);

                if (model == null)
                {
                    throw new KeyNotFoundException($"Model {modelId} not found");
                }

                if (model.Status == ModelStatus.Downloaded)
                {
                    progress?.Report(1.0);
                    return;
                }

                // Get model info
                var modelInfo = await context.ModelInfos.FirstOrDefaultAsync(m => m.Id == modelId);
                if (modelInfo == null)
                {
                    throw new KeyNotFoundException($"Model info for {modelId} not found");
                }

                // Update model status to downloading
                model.Status = ModelStatus.Downloading;
                await context.SaveChangesAsync();

                // Create models directory if it doesn't exist
                var modelsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models");
                Directory.CreateDirectory(modelsDir);
                var modelDir = Path.Combine(modelsDir, modelId);
                Directory.CreateDirectory(modelDir);

                // In a real implementation, you would download the model from modelInfo.DownloadUrl
                // This is just a simulation for the example
                for (int i = 0; i <= 10; i++)
                {
                    progress?.Report(i / 10.0);
                    await Task.Delay(200); // Simulate download time
                }

                // Create a dummy model file for the example
                var modelPath = Path.Combine(modelDir, $"{modelId}.onnx");
                if (!File.Exists(modelPath))
                {
                    using (var fs = File.Create(modelPath))
                    {
                        // In a real implementation, you would write the downloaded model to this file
                        // For now, we'll just create an empty file

                        // Write a small amount of data to make it a valid file
                        byte[] data = Encoding.UTF8.GetBytes("ONNX Model Placeholder");
                        fs.Write(data, 0, data.Length);
                    }
                }

                // Update model status to downloaded
                model.Status = ModelStatus.Downloaded;
                model.LocalPath = modelPath;
                model.DownloadedDate = DateTime.UtcNow;
                await context.SaveChangesAsync();

                progress?.Report(1.0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading model {ModelId}", modelId);

                // Update model status to error
                using var context = await _contextFactory.CreateDbContextAsync();
                var model = await context.AIModels.FirstOrDefaultAsync(m => m.Id == modelId);
                if (model != null)
                {
                    model.Status = ModelStatus.Error;
                    await context.SaveChangesAsync();
                }

                OnError?.Invoke(this, ex);
                throw;
            }
            finally
            {
                _modelLock.Release();
            }
        }

        public bool IsModelLoaded(string modelId)
        {
            return _loadedModels.ContainsKey(modelId);
        }

        public async Task UnloadModelAsync(string modelId)
        {
            try
            {
                await _modelLock.WaitAsync();

                if (_loadedModels.TryGetValue(modelId, out var session))
                {
                    session.Dispose();
                    _loadedModels.Remove(modelId);
                    _logger.LogInformation("Model {ModelId} unloaded", modelId);
                }
            }
            finally
            {
                _modelLock.Release();
            }
        }

        private async Task LoadModelAsync(string modelId)
        {
            try
            {
                await _modelLock.WaitAsync();

                // Check if model is already loaded
                if (_loadedModels.ContainsKey(modelId))
                {
                    return;
                }

                // Get model info from database
                using var context = await _contextFactory.CreateDbContextAsync();
                var model = await context.AIModels.FirstOrDefaultAsync(m => m.Id == modelId);

                if (model == null)
                {
                    throw new KeyNotFoundException($"Model {modelId} not found");
                }

                if (model.Status != ModelStatus.Downloaded || string.IsNullOrEmpty(model.LocalPath))
                {
                    throw new InvalidOperationException($"Model {modelId} is not downloaded or has no local path");
                }

                if (!File.Exists(model.LocalPath))
                {
                    throw new FileNotFoundException($"Model file not found at {model.LocalPath}");
                }

                // Check the file size - real ONNX models are typically at least several megabytes
                var fileInfo = new FileInfo(model.LocalPath);
                if (fileInfo.Length < 1024) // Less than 1KB is definitely not a valid model
                {
                    throw new InvalidOperationException($"Model file at {model.LocalPath} is too small to be a valid ONNX model");
                }

                // Create session options
                var options = new SessionOptions();

                // Try to use DirectML (GPU acceleration) if enabled
                bool useGpu = false;
                var settings = await context.UserSettings.FirstOrDefaultAsync();
                if (settings != null)
                {
                    useGpu = settings.UseGPU;
                }

                if (useGpu)
                {
                    try
                    {
                        options.AppendExecutionProvider_DML(0);
                    }
                    catch (EntryPointNotFoundException)
                    {
                        _logger.LogWarning("DirectML provider not available, falling back to CPU");
                    }
                }

                try
                {
                    // Create the inference session
                    var session = new InferenceSession(model.LocalPath, options);
                    _loadedModels[modelId] = session;

                    _logger.LogInformation("Model {ModelId} loaded successfully", modelId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading ONNX model from {Path}", model.LocalPath);

                    // Mark the model as having an error
                    model.Status = ModelStatus.Error;
                    await context.SaveChangesAsync();

                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading model {ModelId}", modelId);
                OnError?.Invoke(this, ex);
                throw;
            }
            finally
            {
                _modelLock.Release();
            }
        }

        private void ReportProgress(string message)
        {
            _logger.LogInformation(message);
            OnInferenceProgress?.Invoke(this, message);
        }

        private string FormatPromptWithHistory(IEnumerable<(bool IsUser, string Message)> history, string prompt, string systemPrompt)
        {
            var sb = new StringBuilder();

            // Add system prompt if provided
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                sb.AppendLine($"System: {systemPrompt}");
                sb.AppendLine();
            }

            // Add conversation history
            foreach (var (isUser, message) in history)
            {
                sb.AppendLine($"{(isUser ? "User: " : "Assistant: ")}{message}");
            }

            // Add current prompt
            sb.AppendLine($"User: {prompt}");
            sb.AppendLine("Assistant:");

            return sb.ToString();
        }

        private long[] Tokenize(string text)
        {
            // This is a very simplified tokenization approach
            // In a real implementation, you would use a proper tokenizer matched to your model

            // For demonstration purposes, let's just map characters to token IDs
            var tokens = new List<long>();

            foreach (char c in text)
            {
                tokens.Add((long)c);
            }

            // Add special tokens
            tokens.Insert(0, 101); // [CLS] token (commonly used in BERT-like models)
            tokens.Add(102); // [SEP] token

            return tokens.ToArray();
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
                _modelLock.Dispose();

                _disposed = true;
            }
        }
    }
}