using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;
using Nexi.UI.Models;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Collections.Specialized;

namespace Nexi.UI.ViewModels
{
    public class ChatViewModel : ViewModelBase
    {
        private readonly ICommandProcessor _commandProcessor;
        private readonly IVoiceService _voiceService;
        private readonly IChatStorageService _chatStorage;
        private readonly IAIModelService _aiModelService;
        private readonly IUserSettingsService _userSettingsService;
        private readonly ILlamaSharpService _llamaSharpService;
        private readonly ILogger<ChatViewModel> _logger;
        private string _currentMessage = string.Empty;
        private bool _isVoiceModeEnabled;
        private ObservableCollection<ChatMessage> _messages;
        private bool _isProcessing;
        private string _sessionId;
        private string _title;
        private string? _selectedModelId;

        // Event that the view can subscribe to for scrolling to bottom
        public event Action? ScrollToBottom;

        public ChatViewModel(
            ICommandProcessor commandProcessor,
            IVoiceService voiceService,
            IChatStorageService chatStorage,
            IAIModelService aiModelService,
            IUserSettingsService userSettingsService,
            ILlamaSharpService llamaSharpService,
            ILogger<ChatViewModel> logger,
            string? sessionId = null)
        {
            _commandProcessor = commandProcessor;
            _voiceService = voiceService;
            _chatStorage = chatStorage;
            _aiModelService = aiModelService;
            _userSettingsService = userSettingsService;
            _llamaSharpService = llamaSharpService;
            _logger = logger;
            _sessionId = sessionId ?? Guid.NewGuid().ToString();
            _title = "New Chat";
            Messages = new ObservableCollection<ChatMessage>();

            // Initialize commands
            SendMessageCommand = ReactiveCommand.CreateFromTask(SendMessageAsync, this.WhenAnyValue(x => x.HasMessageText));
            ClearMessageCommand = ReactiveCommand.Create(() => CurrentMessage = string.Empty);

            // Load chat history if session ID is provided
            if (!string.IsNullOrEmpty(sessionId))
            {
                _ = LoadChatHistoryAsync(sessionId);
            }

            // Load the selected model from user settings
            _ = LoadSelectedModelAsync();

            // Set up voice recognition if enabled
            _voiceService.SpeechRecognized += OnSpeechRecognized;

            if (sessionId == null)
            {
                // Add welcome message
                _ = AddMessageAsync(new ChatMessage
                {
                    Content = "Hello! I'm Nexi. You can type 'help' to see available commands, or use the microphone button for voice commands.",
                    Timestamp = DateTime.Now,
                    IsUser = false
                });
            }
            else
            {
                // Load existing chat
                _ = LoadChatHistoryAsync(sessionId);
            }

            // Subscribe to voice mode changes
            this.WhenAnyValue(x => x.IsVoiceModeEnabled)
                .Subscribe(async isEnabled =>
                {
                    if (isEnabled)
                    {
                        await _voiceService.StartListeningAsync();
                        await AddMessageAsync(new ChatMessage
                        {
                            Content = "Voice mode enabled. Speak your commands.",
                            Timestamp = DateTime.Now,
                            IsUser = false
                        });
                    }
                    else
                    {
                        await _voiceService.StopListeningAsync();
                        await AddMessageAsync(new ChatMessage
                        {
                            Content = "Voice mode disabled.",
                            Timestamp = DateTime.Now,
                            IsUser = false
                        });
                    }
                });
        }

        public string Title
        {
            get => _title;
            set => this.RaiseAndSetIfChanged(ref _title, value);
        }

        public ObservableCollection<ChatMessage> Messages
        {
            get => _messages;
            set => this.RaiseAndSetIfChanged(ref _messages, value);
        }

        public string CurrentMessage
        {
            get => _currentMessage;
            set
            {
                this.RaiseAndSetIfChanged(ref _currentMessage, value);
                this.RaisePropertyChanged(nameof(HasMessageText));
            }
        }

        public bool IsVoiceModeEnabled
        {
            get => _isVoiceModeEnabled;
            set => this.RaiseAndSetIfChanged(ref _isVoiceModeEnabled, value);
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set => this.RaiseAndSetIfChanged(ref _isProcessing, value);
        }

        public string? SelectedModelId
        {
            get => _selectedModelId;
            set 
            {
                try
                {
                    if (string.IsNullOrEmpty(value))
                    {
                        _logger.LogWarning("Attempted to set null or empty SelectedModelId");
                        return;
                    }

                    // Log the model change
                    _logger.LogInformation($"Setting SelectedModelId to {value}");
                    
                    // Validate the model exists and is downloaded before setting
                    Task.Run(async () =>
                    {
                        try
                        {
                            // Check if model exists
                            var model = await _aiModelService.GetModelAsync(value);
                            if (model == null)
                            {
                                _logger.LogWarning($"Model {value} not found in database");
                                return;
                            }

                            // Check if model is downloaded
                            bool isDownloaded = await _aiModelService.IsModelDownloadedAsync(value);
                            if (!isDownloaded)
                            {
                                _logger.LogWarning($"Model {value} is not downloaded");
                                return;
                            }

                            // Model is valid, set it on the UI thread
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                this.RaiseAndSetIfChanged(ref _selectedModelId, value);
                                _logger.LogInformation($"Successfully set SelectedModelId to {value}");
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error validating model {value}");
                        }
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error setting SelectedModelId");
                }
            }
        }

        public bool HasMessageText => !string.IsNullOrWhiteSpace(CurrentMessage);

        public ICommand SendMessageCommand { get; }
        public ICommand ClearMessageCommand { get; }

        private async Task LoadChatHistoryAsync(string sessionId)
        {
            try
            {
                var session = await _chatStorage.GetSessionAsync(sessionId);
                if (session != null)
                {
                    Title = session.Title;
                    foreach (var message in session.Messages)
                    {
                        Messages.Add(new ChatMessage
                        {
                            Content = message.Content,
                            Timestamp = message.Timestamp,
                            IsUser = message.IsUser
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading chat history");
            }
        }

        private async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(CurrentMessage))
                return;
                
            await SendMessageAsync(CurrentMessage.Trim());
            CurrentMessage = string.Empty;
        }

        private async Task SendMessageAsync(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                return;
            }

            try
            {
                // Set processing state
                IsProcessing = true;

                // Add user message to the chat
                var userChatMessage = new ChatMessage
                {
                    Content = userMessage,
                    Timestamp = DateTime.Now,
                    IsUser = true
                };
                
                await AddMessageAsync(userChatMessage);

                // Check if we have a selected model
                if (string.IsNullOrEmpty(SelectedModelId))
                {
                    _logger.LogWarning("No model selected for inference");
                    await AddMessageAsync(new ChatMessage
                    {
                        Content = "Please select an AI model in the Settings before sending messages.",
                        Timestamp = DateTime.Now,
                        IsUser = false,
                        IsSystemMessage = true
                    });
                    return;
                }

                // Create a placeholder for the assistant's response
                var assistantMessage = new ChatMessage
                {
                    Content = "Thinking...",
                    Timestamp = DateTime.Now,
                    IsUser = false
                };
                
                await AddMessageAsync(assistantMessage);

                // Check if the model is initialized
                bool isInitialized = await _llamaSharpService.IsModelInitializedAsync();
                if (!isInitialized)
                {
                    _logger.LogInformation("Model not initialized, initializing now");
                    
                    // Get the model path
                    string modelPath = await _aiModelService.GetModelLocalPathAsync(SelectedModelId);
                    
                    // Get user settings for context size and GPU layers
                    var settings = await _userSettingsService.GetSettingsAsync();
                    int contextSize = settings.ContextSize;
                    int gpuLayerCount = settings.UseGPU ? settings.GpuLayerCount : 0;
                    
                    // Initialize the model
                    bool initialized = await _llamaSharpService.InitializeModelAsync(modelPath, contextSize, gpuLayerCount);
                    if (!initialized)
                    {
                        _logger.LogError("Failed to initialize model");
                        assistantMessage.Content = "Error: Failed to initialize the AI model. Please try again or select a different model.";
                        
                        // Force UI update
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            this.RaisePropertyChanged(nameof(Messages));
                            ScrollToBottom?.Invoke();
                        });
                        return;
                    }
                }

                // Use the LlamaSharpService directly for streaming responses
                try
                {
                    _logger.LogInformation("Starting chat with message: {Message}", userMessage);
                    
                    // Get the streaming response
                    var responseStream = await _llamaSharpService.ChatAsync(userMessage);
                    
                    // Process the tokens as they arrive
                    StringBuilder responseBuilder = new StringBuilder();
                    int tokenCount = 0;
                    
                    await foreach (var token in responseStream)
                    {
                        tokenCount++;
                        
                        // Append the token to our response
                        responseBuilder.Append(token);
                        
                        // Update the UI with the current response immediately
                        assistantMessage.Content = responseBuilder.ToString();
                        
                        // Force UI update on the UI thread for every token
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            // Explicitly raise property changed for the Messages collection
                            this.RaisePropertyChanged(nameof(Messages));
                            
                            // Also raise property changed for the specific message
                            int index = Messages.IndexOf(assistantMessage);
                            if (index >= 0)
                            {
                                // Create a temporary copy of the message
                                var updatedMessage = new ChatMessage
                                {
                                    Content = assistantMessage.Content,
                                    Timestamp = assistantMessage.Timestamp,
                                    IsUser = assistantMessage.IsUser,
                                    IsSystemMessage = assistantMessage.IsSystemMessage
                                };
                                
                                // Replace the message in the collection
                                Messages[index] = updatedMessage;
                                
                                // Update our reference
                                assistantMessage = updatedMessage;
                            }
                            
                            // Scroll to bottom to show the latest content
                            ScrollToBottom?.Invoke();
                        });
                        
                        // Log every 10 tokens for debugging
                        if (tokenCount % 10 == 0)
                        {
                            _logger.LogDebug("Received {Count} tokens. Current response: {Response}", 
                                tokenCount, responseBuilder.ToString());
                        }
                    }
                    
                    _logger.LogInformation("Chat completed. Total tokens: {Count}", tokenCount);
                    
                    // Final update to ensure content is set
                    if (tokenCount > 0)
                    {
                        assistantMessage.Content = responseBuilder.ToString();
                        
                        // Force UI update
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            this.RaisePropertyChanged(nameof(Messages));
                            ScrollToBottom?.Invoke();
                        });
                        
                        // Save the assistant message to storage
                        await _chatStorage.AddMessageAsync(_sessionId, new ChatMessageData
                        {
                            Content = assistantMessage.Content,
                            IsUser = false,
                            Timestamp = DateTime.Now
                        });

                        // Update the chat title if this is a new chat
                        if (Messages.Count <= 2 && Title == "New Chat")
                        {
                            var newTitle = GenerateTitleFromMessages();
                            await UpdateSessionTitleAsync(_sessionId, newTitle);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("No tokens were generated in the response");
                        assistantMessage.Content = "No response was generated. Please try again.";
                        
                        // Force UI update
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            this.RaisePropertyChanged(nameof(Messages));
                            ScrollToBottom?.Invoke();
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during chat: {ErrorMessage}", ex.Message);
                    
                    // Update the assistant message with the error
                    assistantMessage.Content = $"Error: {ex.Message}";
                    
                    // Force UI update
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        this.RaisePropertyChanged(nameof(Messages));
                        ScrollToBottom?.Invoke();
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SendMessageAsync: {ErrorMessage}", ex.Message);
                
                // Add error message to the chat
                await AddMessageAsync(new ChatMessage
                {
                    Content = $"Error: {ex.Message}",
                    IsUser = false,
                    IsSystemMessage = true,
                    Timestamp = DateTime.Now
                });
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private void OnSpeechRecognized(object? sender, string text)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                await ProcessInputAsync(text);
            });
        }

        private async Task ProcessInputAsync(string input)
        {
            IsProcessing = true;
            
            try
            {
                // Add user's message
                await AddMessageAsync(new ChatMessage
                {
                    Content = input,
                    Timestamp = DateTime.Now,
                    IsUser = true
                });

                // Auto-set title if this is the first user message
                if (Title == "New Chat" && Messages.Count <= 3)
                {
                    Title = input.Length > 25 ? input.Substring(0, 22) + "..." : input;
                    await UpdateSessionTitleAsync(_sessionId, Title);
                }

                // Process message
                string response;
                
                // Check if we should use AI model or command processor
                if (!string.IsNullOrEmpty(SelectedModelId) && await _aiModelService.IsModelDownloadedAsync(SelectedModelId))
                {
                    // Create a response message placeholder
                    var responseMessage = new ChatMessage
                    {
                        Content = "Thinking...",
                        Timestamp = DateTime.Now,
                        IsUser = false
                    };
                    
                    await AddMessageAsync(responseMessage);
                    
                    // Use the AI model for generating a response
                    string generatedText = "";
                    
                    await _aiModelService.RunModelInferenceAsync(
                        SelectedModelId,
                        input,
                        token => 
                        {
                            generatedText += token;
                            
                            // Update the message content as tokens arrive
                            Dispatcher.UIThread.Post(() => 
                            {
                                responseMessage.Content = generatedText;
                                this.RaisePropertyChanged(nameof(Messages));
                            });
                        });
                    
                    // Remove the placeholder message
                    Messages.Remove(responseMessage);
                    
                    // Add the final response
                    await AddMessageAsync(new ChatMessage
                    {
                        Content = generatedText,
                        Timestamp = DateTime.Now,
                        IsUser = false
                    });
                }
                else if (_commandProcessor.IsCommand(input))
                {
                    response = _commandProcessor.ProcessCommand(input);
                    
                    // Add response
                    await AddMessageAsync(new ChatMessage
                    {
                        Content = response,
                        Timestamp = DateTime.Now,
                        IsUser = false
                    });
                }
                else
                {
                    response = "I need an AI model to respond to that. Please select a model in the Settings or use a command. Type 'help' to see available commands.";
                    
                    // Add response
                    await AddMessageAsync(new ChatMessage
                    {
                        Content = response,
                        Timestamp = DateTime.Now,
                        IsUser = false
                    });
                }
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task AddMessageAsync(ChatMessage message)
        {
            Messages.Add(message);

            // Convert to storage message
            var messageData = new ChatMessageData
            {
                Content = message.Content,
                Timestamp = message.Timestamp,
                IsUser = message.IsUser
            };

            try
            {
                var session = await _chatStorage.GetSessionAsync(_sessionId);
                if (session == null)
                {
                    // Create new session
                    session = await _chatStorage.CreateSessionAsync(Title);
                    _sessionId = session.Id;
                }

                await _chatStorage.AddMessageAsync(_sessionId, messageData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving message: {Message}", ex.Message);
            }
        }

        private async Task UpdateSessionTitleAsync(string sessionId, string title)
        {
            try
            {
                var session = await _chatStorage.GetSessionAsync(sessionId);
                if (session != null)
                {
                    session.Title = title;
                    await _chatStorage.SaveSessionAsync(session);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating session title: {Message}", ex.Message);
            }
        }

        private async Task LoadSelectedModelAsync()
        {
            try
            {
                _logger.LogInformation("Loading selected model from user settings");
                
                // Get user settings
                var settings = await _userSettingsService.GetSettingsAsync();
                if (settings != null && !string.IsNullOrEmpty(settings.SelectedModelId))
                {
                    _logger.LogInformation($"Found selected model ID in settings: {settings.SelectedModelId}");
                    
                    // Check if the model exists and is downloaded
                    var model = await _aiModelService.GetModelAsync(settings.SelectedModelId);
                    if (model != null)
                    {
                        bool isDownloaded = await _aiModelService.IsModelDownloadedAsync(settings.SelectedModelId);
                        if (isDownloaded)
                        {
                            _logger.LogInformation($"Setting selected model to {settings.SelectedModelId}");
                            SelectedModelId = settings.SelectedModelId;
                            
                            // Add a system message indicating the selected model
                            await AddMessageAsync(new ChatMessage
                            {
                                Content = $"Using AI model: {model.Name}",
                                Timestamp = DateTime.Now,
                                IsUser = false,
                                IsSystemMessage = true
                            });
                        }
                        else
                        {
                            _logger.LogWarning($"Selected model {settings.SelectedModelId} is not downloaded");
                            
                            // Add a system message indicating the model is not downloaded
                            await AddMessageAsync(new ChatMessage
                            {
                                Content = $"The selected model '{model.Name}' is not downloaded. Please go to Models and download it first.",
                                Timestamp = DateTime.Now,
                                IsUser = false,
                                IsSystemMessage = true
                            });
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"Selected model {settings.SelectedModelId} not found");
                    }
                }
                else
                {
                    _logger.LogInformation("No selected model found in settings");
                    
                    // Add a system message indicating no model is selected
                    await AddMessageAsync(new ChatMessage
                    {
                        Content = "No AI model selected. Please go to Settings to select a model.",
                        Timestamp = DateTime.Now,
                        IsUser = false,
                        IsSystemMessage = true
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading selected model");
            }
        }

        private string GenerateTitleFromMessages()
        {
            // Generate a title based on the first user message
            var firstUserMessage = Messages.FirstOrDefault(m => m.IsUser)?.Content ?? "New Chat";
            
            // Truncate to a reasonable length
            if (firstUserMessage.Length > 30)
            {
                firstUserMessage = firstUserMessage.Substring(0, 27) + "...";
            }
            
            return firstUserMessage;
        }

        private async Task ProcessCommandAsync(string command)
        {
            // Trim the command and remove the leading slash
            command = command.Trim();
            if (command.StartsWith("/"))
            {
                command = command.Substring(1);
            }

            // Add the command to the chat
            var userMessage = new ChatMessage
            {
                Content = "/" + command,
                IsUser = true,
                Timestamp = DateTime.Now
            };
            Messages.Add(userMessage);

            // Clear the input
            CurrentMessage = string.Empty;

            // Process the command
            var result = _commandProcessor.ProcessCommand(command);

            // Add the result to the chat
            var responseMessage = new ChatMessage
            {
                Content = result,
                IsUser = false,
                IsSystemMessage = true,
                Timestamp = DateTime.Now
            };
            Messages.Add(responseMessage);

            // Save the command and response to storage
            await _chatStorage.AddMessageAsync(_sessionId, new ChatMessageData
            {
                Content = "/" + command,
                IsUser = true,
                Timestamp = DateTime.Now
            });

            await _chatStorage.AddMessageAsync(_sessionId, new ChatMessageData
            {
                Content = result,
                IsUser = false,
                Timestamp = DateTime.Now
            });

            // Scroll to bottom
            ScrollToBottom?.Invoke();
        }

        public override void Dispose()
        {
            _voiceService.SpeechRecognized -= OnSpeechRecognized;
            base.Dispose();
        }
    }
}