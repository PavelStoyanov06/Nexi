using LLama;
using LLama.Common;
using Microsoft.Extensions.Logging;
using Nexi.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nexi.Services
{
    public class LlamaSharpService : ILlamaSharpService, IDisposable
    {
        private readonly ILogger<LlamaSharpService> _logger;
        private LLamaWeights? _model;
        private LLamaContext? _context;
        private InteractiveExecutor? _executor;
        private ChatSession? _session;
        private bool _isInitialized = false;
        private bool _disposedValue;

        public LlamaSharpService(ILogger<LlamaSharpService> logger)
        {
            _logger = logger;
        }

        public async Task<bool> InitializeModelAsync(string modelPath, int contextSize, int gpuLayerCount)
        {
            try
            {
                // Dispose of any existing resources
                DisposeResources();

                _logger.LogInformation("Initializing LlamaSharp model from {ModelPath}", modelPath);
                
                var parameters = new ModelParams(modelPath)
                {
                    ContextSize = (uint?)contextSize,
                    GpuLayerCount = gpuLayerCount
                };

                // Load model asynchronously
                await Task.Run(() =>
                {
                    _model = LLamaWeights.LoadFromFile(parameters);
                    _context = _model.CreateContext(parameters);
                    _executor = new InteractiveExecutor(_context);
                });

                // Initialize chat session with system prompt
                var chatHistory = new ChatHistory();
                chatHistory.AddMessage(AuthorRole.System, "You are Nexi, a helpful, kind, honest, and precise AI assistant. You always provide accurate information and assist users with their questions and tasks.");
                
                _session = new ChatSession(_executor, chatHistory);
                _isInitialized = true;
                
                _logger.LogInformation("LlamaSharp model initialized successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing LlamaSharp model");
                _isInitialized = false;
                return false;
            }
        }

        public async Task<string> GenerateResponseAsync(string prompt, Action<string> onTokenGenerated)
        {
            if (!_isInitialized || _session == null)
            {
                throw new InvalidOperationException("LlamaSharp model is not initialized");
            }

            try
            {
                var inferenceParams = new InferenceParams
                {
                    MaxTokens = 512,
                    AntiPrompts = new List<string> { "User:" }
                };

                var fullResponse = new List<string>();
                
                await foreach (var token in _session.ChatAsync(
                    new ChatHistory.Message(AuthorRole.User, prompt),
                    inferenceParams))
                {
                    onTokenGenerated(token);
                    fullResponse.Add(token);
                }

                return string.Concat(fullResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating response");
                throw;
            }
        }

        public async Task<IAsyncEnumerable<string>> ChatAsync(string userMessage, ChatHistory? history = null)
        {
            if (!_isInitialized || _session == null)
            {
                throw new InvalidOperationException("LlamaSharp model is not initialized");
            }

            try
            {
                // If history is provided, create a new session with that history
                if (history != null && history.Messages.Count > 0)
                {
                    _session = new ChatSession(_executor!, history);
                }

                var inferenceParams = new InferenceParams
                {
                    MaxTokens = 512,
                    AntiPrompts = new List<string> { "User:" }
                };

                return _session.ChatAsync(
                    new ChatHistory.Message(AuthorRole.User, userMessage),
                    inferenceParams);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in chat");
                throw;
            }
        }

        public Task<bool> IsModelInitializedAsync()
        {
            return Task.FromResult(_isInitialized);
        }

        private void DisposeResources()
        {
            _session = null;
            _executor = null;
            _context?.Dispose();
            _context = null;
            _model?.Dispose();
            _model = null;
            _isInitialized = false;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    DisposeResources();
                }
                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
} 