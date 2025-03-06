using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Nexi.Services.Interfaces;
using Nexi.UI.Models;
using Avalonia.Threading;
using System.Threading.Tasks;
using Nexi.Data.Models;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;

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
            if (string.IsNullOrWhiteSpace(CurrentMessage) || IsProcessing)
                return;

            try
            {
                IsProcessing = true;

                // Get the user's message
                var userMessage = CurrentMessage.Trim();
                CurrentMessage = string.Empty;

                // Add user message to the chat
                var userChatMessage = new ChatMessage
                {
                    Content = userMessage,
                    IsUser = true,
                    Timestamp = DateTime.Now
                };
                Messages.Add(userChatMessage);

                // Save the message to storage
                await _chatStorage.AddMessageAsync(_sessionId, new ChatMessageData
                {
                    Content = userMessage,
                    IsUser = true,
                    Timestamp = DateTime.Now
                });

                // Get settings to check if a model is selected
                var settings = await _userSettingsService.GetSettingsAsync();
                if (string.IsNullOrEmpty(settings.SelectedModelId))
                {
                    // No model selected, show error message
                    var errorMessage = new ChatMessage
                    {
                        Content = "No AI model is selected. Please go to the Models page to download and select a model.",
                        IsUser = false,
                        Timestamp = DateTime.Now
                    };
                    Messages.Add(errorMessage);
                    await _chatStorage.AddMessageAsync(_sessionId, new ChatMessageData
                    {
                        Content = "No AI model is selected. Please go to the Models page to download and select a model.",
                        IsUser = false,
                        Timestamp = DateTime.Now
                    });
                    return;
                }

                // Create a placeholder for the assistant's response
                var assistantMessage = new ChatMessage
                {
                    Content = "",
                    IsUser = false,
                    Timestamp = DateTime.Now
                };
                Messages.Add(assistantMessage);

                // Process with LlamaSharp
                var responseBuilder = new StringBuilder();
                
                // Create a token handler that updates the UI
                Action<string> onTokenGenerated = (token) =>
                {
                    responseBuilder.Append(token);
                    
                    // Update the message on the UI thread
                    Dispatcher.UIThread.Post(() =>
                    {
                        assistantMessage.Content = responseBuilder.ToString();
                        this.RaisePropertyChanged(nameof(Messages));
                    });
                };

                // Run the inference
                var success = await _aiModelService.RunModelInferenceAsync(
                    settings.SelectedModelId,
                    userMessage,
                    onTokenGenerated);

                if (!success)
                {
                    // If inference failed, update the message
                    assistantMessage.Content = "Sorry, I encountered an error processing your request. Please try again or check if the model is properly loaded.";
                }

                // Save the assistant's message
                await _chatStorage.AddMessageAsync(_sessionId, new ChatMessageData
                {
                    Content = responseBuilder.ToString(),
                    IsUser = false,
                    Timestamp = DateTime.Now
                });

                // Update the title if this is a new chat
                if (Messages.Count <= 3 && _title == "New Chat")
                {
                    _title = GenerateTitleFromMessages();
                    var session = await _chatStorage.GetSessionAsync(_sessionId);
                    if (session != null)
                    {
                        session.Title = _title;
                        await _chatStorage.SaveSessionAsync(session);
                        this.RaisePropertyChanged(nameof(Title));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message");
                
                // Add error message to chat
                var errorMessage = new ChatMessage
                {
                    Content = $"An error occurred: {ex.Message}",
                    IsUser = false,
                    Timestamp = DateTime.Now
                };
                Messages.Add(errorMessage);
                await _chatStorage.AddMessageAsync(_sessionId, new ChatMessageData
                {
                    Content = $"An error occurred: {ex.Message}",
                    IsUser = false,
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

        public override void Dispose()
        {
            _voiceService.SpeechRecognized -= OnSpeechRecognized;
            base.Dispose();
        }
    }
}