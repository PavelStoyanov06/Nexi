using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LLama;
using LLama.Common;
using Microsoft.Extensions.Logging;
using Nexi.Services.Interfaces;
using LLama.Sampling;
using System.Linq;
using System.Text;

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
        private string? _currentModelPath;
        private int _currentContextSize;
        private int _currentGpuLayerCount;
        private readonly SemaphoreSlim _modelLock = new SemaphoreSlim(1, 1);
        private const int MAX_SAFE_GPU_LAYERS = 20; // Reduced from 5 to be even safer
        
        // Cache for model parameters to avoid unnecessary reinitialization
        private ModelParams? _lastParams;
        
        // Track initialization attempts to prevent infinite retry loops
        private int _initializationAttempts = 0;
        private const int MAX_INIT_ATTEMPTS = 3;

        public LlamaSharpService(ILogger<LlamaSharpService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            try
            {
                // Configure native library loading
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string runtimesDir = Path.Combine(baseDir, "runtimes");
                
                string platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
                                 RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
                                 RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" : "unknown";
                
                string arch = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "x64" :
                             RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x86";
                
                string nativeLibPath = Path.Combine(runtimesDir, $"{platform}-{arch}", "native");
                
                if (Directory.Exists(nativeLibPath))
                {
                    _logger.LogInformation("Setting native library path to: {Path}", nativeLibPath);
                    
                    // Set environment variable to help locate native libraries
                    Environment.SetEnvironmentVariable("PATH", 
                        Environment.GetEnvironmentVariable("PATH") + Path.PathSeparator + nativeLibPath);
                    
                    // Check for llama.dll in subdirectories (avx, noavx, etc.)
                    string[] subDirs = { "avx", "avx2", "avx512", "noavx" };
                    bool foundLlamaDll = false;
                    
                    foreach (var subDir in subDirs)
                    {
                        string subDirPath = Path.Combine(nativeLibPath, subDir);
                        string llamaDllPath = Path.Combine(subDirPath, GetLibraryName());
                        
                        if (File.Exists(llamaDllPath))
                        {
                            _logger.LogInformation("Found llama.dll in subdirectory: {SubDir}", subDir);
                            
                            // Add this subdirectory to PATH as well
                            Environment.SetEnvironmentVariable("PATH", 
                                Environment.GetEnvironmentVariable("PATH") + Path.PathSeparator + subDirPath);
                            
                            // Copy the DLL to the native directory for direct access
                            try
                            {
                                string targetPath = Path.Combine(nativeLibPath, GetLibraryName());
                                if (!File.Exists(targetPath))
                                {
                                    File.Copy(llamaDllPath, targetPath);
                                    _logger.LogInformation("Copied llama.dll to native directory");
                                }
                                foundLlamaDll = true;
                                break;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to copy llama.dll to native directory. Will use subdirectory path instead.");
                                foundLlamaDll = true;
                                break;
                            }
                        }
                    }
                    
                    if (!foundLlamaDll)
                    {
                        _logger.LogWarning("Could not find llama.dll in any subdirectory");
                    }
                    
                    // Note: NativeLibraryConfig is not available in your version of LlamaSharp
                    // We'll use environment variables instead
                }
                else
                {
                    _logger.LogWarning("Native library path not found: {Path}", nativeLibPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error configuring native library loading");
            }
        }
        
        private string GetLibraryName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return "llama.dll";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return "libllama.so";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return "libllama.dylib";
            else
                throw new PlatformNotSupportedException("Unsupported platform");
        }

        public async Task<bool> InitializeModelAsync(string modelPath, int contextSize, int gpuLayerCount)
        {
            if (string.IsNullOrEmpty(modelPath))
            {
                _logger.LogError("Model path cannot be null or empty");
                return false;
            }
            
            // Reset initialization attempts if this is a new model or parameters
            if (_currentModelPath != modelPath || _currentContextSize != contextSize || _currentGpuLayerCount != gpuLayerCount)
            {
                _initializationAttempts = 0;
            }
            
            // Check if we've exceeded the maximum number of initialization attempts
            if (_initializationAttempts >= MAX_INIT_ATTEMPTS)
            {
                _logger.LogError("Exceeded maximum number of initialization attempts ({Max})", MAX_INIT_ATTEMPTS);
                return false;
            }
            
            _initializationAttempts++;

            // Limit GPU layers to a safe maximum to prevent memory issues
            if (gpuLayerCount > MAX_SAFE_GPU_LAYERS)
            {
                _logger.LogWarning("Requested GPU layer count {RequestedCount} exceeds safe maximum of {MaxSafe}. Reducing to safe value.", 
                    gpuLayerCount, MAX_SAFE_GPU_LAYERS);
                gpuLayerCount = MAX_SAFE_GPU_LAYERS;
            }

            try
            {
                // Use a semaphore to ensure only one thread can initialize the model at a time
                await _modelLock.WaitAsync();

                // Check if we're already initialized with the same parameters
                if (_isInitialized && 
                    _currentModelPath == modelPath && 
                    _currentContextSize == contextSize && 
                    _currentGpuLayerCount == gpuLayerCount)
                {
                    _logger.LogInformation("Model already initialized with the same parameters");
                    return true;
                }

                // If we're already initialized but with different parameters, dispose the current resources
                if (_isInitialized)
                {
                    _logger.LogInformation("Disposing existing model resources before reinitializing");
                    DisposeResources();
                }

                _logger.LogInformation("Initializing LlamaSharp model from {ModelPath} with contextSize={ContextSize}, gpuLayerCount={GpuLayerCount}", 
                    modelPath, contextSize, gpuLayerCount);

                // Create model parameters with explicit memory settings
                var parameters = new ModelParams(modelPath)
                {
                    ContextSize = (uint)contextSize,
                    GpuLayerCount = gpuLayerCount,
                    BatchSize = 512,
                    MainGpu = 0,     // Use the primary GPU
                    UseMemorymap = true,
                    UseMemoryLock = false
                    // F16Memory is not available in your version of LlamaSharp
                };

                _lastParams = parameters;

                // Load the model with explicit exception handling
                try
                {
                    _model = LLamaWeights.LoadFromFile(parameters);
                }
                catch (DllNotFoundException ex)
                {
                    _logger.LogError(ex, "Failed to load native library. Please check if the correct version is installed");
                    return false;
                }
                catch (AccessViolationException ex)
                {
                    _logger.LogError(ex, "Access violation during model loading. Reducing GPU layers and retrying");
                    
                    // Force cleanup
                    DisposeResources();
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    
                    // Retry with reduced GPU layers
                    if (gpuLayerCount > 0)
                    {
                        _logger.LogWarning("Retrying with reduced GPU layers");
                        return await InitializeModelAsync(modelPath, contextSize, Math.Max(0, gpuLayerCount - 1));
                    }
                    
                    return false;
                }
                
                // Create the context with explicit exception handling
                try
                {
                    _context = _model.CreateContext(parameters);
                }
                catch (AccessViolationException ex)
                {
                    _logger.LogError(ex, "Access violation during context creation. Reducing context size and retrying");
                    
                    // Force cleanup
                    DisposeResources();
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    
                    // Retry with reduced context size
                    if (contextSize > 512)
                    {
                        _logger.LogWarning("Retrying with reduced context size");
                        return await InitializeModelAsync(modelPath, Math.Max(512, contextSize / 2), gpuLayerCount);
                    }
                    
                    return false;
                }
                
                // Create the executor
                _executor = new InteractiveExecutor(_context);
                
                // Create the session with a system prompt
                var chatHistory = new ChatHistory();
                chatHistory.AddMessage(AuthorRole.System, "You are Nexi, a helpful, kind, honest, and precise AI assistant. You always provide accurate information and assist users with their questions and tasks.");
                _session = new ChatSession(_executor, chatHistory);

                // Update current parameters
                _currentModelPath = modelPath;
                _currentContextSize = contextSize;
                _currentGpuLayerCount = gpuLayerCount;
                _isInitialized = true;

                _logger.LogInformation("Model initialized successfully");
                
                // Force garbage collection to clean up any lingering resources
                GC.Collect();
                GC.WaitForPendingFinalizers();
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize model: {ErrorMessage}", ex.Message);
                
                // Clean up any partially initialized resources
                DisposeResources();
                
                // Force garbage collection to clean up any lingering resources
                GC.Collect();
                GC.WaitForPendingFinalizers();
                
                _isInitialized = false;
                return false;
            }
            finally
            {
                _modelLock.Release();
            }
        }

        public async Task<string> GenerateResponseAsync(string prompt, Action<string> onTokenGenerated)
        {
            if (string.IsNullOrEmpty(prompt))
            {
                _logger.LogWarning("Prompt cannot be null or empty");
                return string.Empty;
            }

            if (!_isInitialized || _executor == null || _context == null)
            {
                string errorMessage = "Model is not initialized. Please initialize the model first.";
                _logger.LogError(errorMessage);
                onTokenGenerated?.Invoke(errorMessage);
                return errorMessage;
            }

            try
            {
                await _modelLock.WaitAsync();
                
                try
                {
                    // Create a new chat session if needed
                    if (_session == null)
                    {
                        _session = new ChatSession(_executor);
                    }

                    // Use a cancellation token with a timeout to prevent hanging
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                    
                    // Create a token handler that tracks if we received any tokens
                    Action<string> tokenHandler = token =>
                    {
                        onTokenGenerated?.Invoke(token);
                    };
                    
                    // Generate the response
                    var responseTokens = new List<string>();
                    
                    // Process the tokens as they come in
                    await foreach (var token in _session.ChatAsync(
                        new ChatHistory.Message(AuthorRole.User, prompt),
                        new InferenceParams()
                        {
                            SamplingPipeline = new DefaultSamplingPipeline()
                            {
                                Temperature = 0.6f,
                                TopP = 0.9f,
                                RepeatPenalty = 1.1f
                            },
                            MaxTokens = 2048,
                            AntiPrompts = new List<string> { "User:", "Human:" }
                        },
                        cts.Token))
                    {
                        // Add to our collection
                        responseTokens.Add(token);
                        
                        // Call the token handler
                        tokenHandler(token);
                    }
                    
                    // If we didn't receive any tokens, something went wrong
                    if (responseTokens.Count == 0)
                    {
                        _logger.LogWarning("No tokens were generated during inference");
                        onTokenGenerated?.Invoke("No response was generated. This could be due to a problem with the model or the native library.");
                    }
                    
                    // Return the full response as a string
                    return string.Join("", responseTokens);
                }
                catch (DllNotFoundException dllEx)
                {
                    _logger.LogError(dllEx, "Native library not found or could not be loaded during inference");
                    onTokenGenerated?.Invoke("\n\nError: Native library not found or could not be loaded. Please ensure LlamaSharp native libraries are installed correctly.");
                    
                    // Reset initialization state
                    _isInitialized = false;
                    return "Error: Native library not found or could not be loaded";
                }
                catch (AccessViolationException avEx)
                {
                    _logger.LogError(avEx, "Access violation during inference. This is likely due to a memory issue with the LlamaSharp native library.");
                    onTokenGenerated?.Invoke("\n\nError: Access violation during inference. This is likely due to a memory issue with the LlamaSharp native library.");
                    
                    // Reset initialization state and dispose resources
                    _isInitialized = false;
                    DisposeResources();
                    return "Error: Access violation during inference";
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Inference operation was cancelled or timed out");
                    onTokenGenerated?.Invoke("\n\nResponse generation was cancelled or timed out.");
                    return "Response generation was cancelled or timed out";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error generating response: {Message}", ex.Message);
                    onTokenGenerated?.Invoke($"\n\nError generating response: {ex.Message}");
                    return $"Error generating response: {ex.Message}";
                }
                finally
                {
                    _modelLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in GenerateResponseAsync: {Message}", ex.Message);
                onTokenGenerated?.Invoke($"\n\nUnexpected error: {ex.Message}");
                return $"Unexpected error: {ex.Message}";
            }
        }

        public async Task<IAsyncEnumerable<string>> ChatAsync(string userMessage, ChatHistory? history = null)
        {
            if (string.IsNullOrEmpty(userMessage))
            {
                _logger.LogWarning("User message is null or empty");
                return EmptyAsyncEnumerable("I didn't receive any input. Please try again.");
            }

            try
            {
                await _modelLock.WaitAsync();

                if (!_isInitialized || _session == null || _executor == null)
                {
                    _logger.LogError("Model is not initialized");
                    _modelLock.Release();
                    return EmptyAsyncEnumerable("Error: Model is not initialized. Please try again.");
                }

                _logger.LogInformation("Processing chat message: {UserMessage}", userMessage);

                // If history is provided, create a new session with that history
                if (history != null && history.Messages.Count > 0)
                {
                    _logger.LogInformation("Setting chat history with {Count} messages", history.Messages.Count);
                    _session = new ChatSession(_executor, history);
                }

                // Use a cancellation token with a timeout to prevent hanging
                var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                
                var inferenceParams = new InferenceParams
                {
                    MaxTokens = 2048,
                    SamplingPipeline = new DefaultSamplingPipeline
                    {
                        Temperature = 0.6f,
                        TopP = 0.9f,
                        RepeatPenalty = 1.1f
                    },
                    AntiPrompts = new List<string> { "User:", "Human:" }
                };
                
                // Create a message with the user's input
                var message = new ChatHistory.Message(AuthorRole.User, userMessage);
                
                try
                {
                    // Return the chat response stream
                    var responseStream = _session.ChatAsync(message, inferenceParams, cts.Token);
                    
                    // Create a wrapper that ensures the lock is released
                    return WrapResponseStream(responseStream, cts, _modelLock);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating chat response stream: {ErrorMessage}", ex.Message);
                    _modelLock.Release();
                    return EmptyAsyncEnumerable($"Error: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in chat: {ErrorMessage}", ex.Message);
                
                // If we get an AccessViolationException, try to reinitialize with reduced GPU layers
                if (ex is AccessViolationException && _lastParams != null && _currentGpuLayerCount > 0)
                {
                    _logger.LogWarning("AccessViolationException occurred. Attempting to reinitialize with reduced GPU layers");
                    
                    // Clean up resources
                    DisposeResources();
                    
                    // Force garbage collection
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    
                    // Set initialized to false to force reinitialization
                    _isInitialized = false;
                }
                
                _modelLock.Release();
                return EmptyAsyncEnumerable($"Error in chat: {ex.Message}");
            }
        }

        private async IAsyncEnumerable<string> WrapResponseStream(
            IAsyncEnumerable<string> responseStream, 
            CancellationTokenSource cts,
            SemaphoreSlim lockObj)
        {
            try
            {
                // Manually collect tokens from the stream
                List<string> responseTokens = new List<string>();
                StringBuilder fullResponse = new StringBuilder();
                bool stopYielding = false;
                
                await foreach (var token in responseStream)
                {
                    _logger.LogDebug("Token received: {Token}", token);
                    responseTokens.Add(token);
                    
                    // If we've already found an anti-prompt, stop yielding tokens
                    if (stopYielding)
                    {
                        continue;
                    }
                    
                    // Add the token to our full response
                    fullResponse.Append(token);
                    string currentResponse = fullResponse.ToString();
                    
                    // Check for anti-prompts (User:, Human:)
                    bool foundAntiPrompt = false;
                    string cleanToken = token;
                    
                    foreach (var antiPrompt in new[] { "User:", "Human:" })
                    {
                        if (currentResponse.EndsWith(antiPrompt, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogDebug("Anti-prompt detected: {AntiPrompt}", antiPrompt);
                            foundAntiPrompt = true;
                            
                            // Calculate how much of the anti-prompt is in the current token
                            int overlapLength = 0;
                            for (int i = 1; i <= antiPrompt.Length && i <= token.Length; i++)
                            {
                                if (currentResponse.EndsWith(antiPrompt.Substring(0, i), StringComparison.OrdinalIgnoreCase))
                                {
                                    overlapLength = i;
                                }
                            }
                            
                            // Remove the anti-prompt portion from the token
                            if (overlapLength > 0)
                            {
                                cleanToken = token.Substring(0, token.Length - overlapLength);
                                _logger.LogDebug("Cleaned token: '{CleanToken}' (removed {OverlapLength} characters)", 
                                    cleanToken, overlapLength);
                            }
                            
                            break;
                        }
                    }
                    
                    // If we found an anti-prompt, this is the last token we'll yield
                    if (foundAntiPrompt)
                    {
                        stopYielding = true;
                        
                        // Only yield the clean token if it's not empty
                        if (!string.IsNullOrEmpty(cleanToken))
                        {
                            yield return cleanToken;
                        }
                    }
                    else
                    {
                        // Otherwise, yield the token as-is
                        yield return token;
                    }
                }
                
                // Check if we received any tokens
                if (responseTokens.Count == 0)
                {
                    _logger.LogWarning("No tokens were generated in the response");
                    yield return "No response was generated. Please try again.";
                }
            }
            finally
            {
                cts.Dispose();
                lockObj.Release();
            }
        }

        public Task<bool> IsModelInitializedAsync()
        {
            return Task.FromResult(_isInitialized);
        }

        private void DisposeResources()
        {
            try
            {
                _logger.LogInformation("Disposing LlamaSharp resources");
                
                // Clear session first
                _session = null;
                
                // Clear executor
                _executor = null;
                
                // Dispose context with exception handling
                if (_context != null)
                {
                    try
                    {
                        _context.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error disposing context: {ErrorMessage}", ex.Message);
                    }
                    finally
                    {
                        _context = null;
                    }
                }
                
                // Dispose model with exception handling
                if (_model != null)
                {
                    try
                    {
                        _model.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error disposing model: {ErrorMessage}", ex.Message);
                    }
                    finally
                    {
                        _model = null;
                    }
                }
                
                _isInitialized = false;
                _currentModelPath = null;
                _currentContextSize = 0;
                _currentGpuLayerCount = 0;
                _lastParams = null;
                _initializationAttempts = 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing resources: {ErrorMessage}", ex.Message);
            }
            finally
            {
                // Force garbage collection to clean up any lingering resources
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect(); // Second collection to ensure finalizers are run
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    DisposeResources();
                    _modelLock.Dispose();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private static async IAsyncEnumerable<string> EmptyAsyncEnumerable(string? errorMessage = null)
        {
            if (!string.IsNullOrEmpty(errorMessage))
            {
                yield return errorMessage;
            }
            
            await Task.CompletedTask;
        }
    }
} 