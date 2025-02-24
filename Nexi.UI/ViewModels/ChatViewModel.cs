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
using System.Collections.Generic;

namespace Nexi.UI.ViewModels
{
    public class ChatViewModel : ViewModelBase
    {
        private readonly ICommandProcessor _commandProcessor;
        private readonly IVoiceService _voiceService;
        private readonly IChatStorageService _chatStorage;
        private readonly IAIService _aiService;
        private readonly IAIModelService _aiModelService;
        private readonly IUserSettingsService _userSettingsService;
        private readonly ILogger<ChatViewModel> _logger;
        private string _currentMessage = string.Empty;
        private bool _isVoiceModeEnabled;
        private ObservableCollection<ChatMessage> _messages;
        private bool _isProcessing;
        private string _sessionId;
        private string _title;
        private AIModelData? _selectedModel;
        private bool _isAiEnabled = true;
        private string _statusMessage = string.Empty;

        public ChatViewModel(
            ICommandProcessor commandProcessor,
            IVoiceService voiceService,
            IChatStorageService chatStorage,
            IAIService aiService,
            IAIModelService aiModelService,
            IUserSettingsService userSettingsService,
            ILogger<ChatViewModel> logger,
            string? sessionId = null)
        {
            _commandProcessor = commandProcessor;
            _voiceService = voiceService;
            _chatStorage = chatStorage;
            _aiService = aiService;
            _aiModelService = aiModelService;
            _userSettingsService = userSettingsService;
            _logger = logger;
            _sessionId = sessionId ?? Guid.NewGuid().ToString();
            _title = "New Chat";
            Messages = new ObservableCollection<ChatMessage>();

            // Initialize commands
            SendMessageCommand = ReactiveCommand.CreateFromTask(SendMessageAsync);
            ClearMessageCommand = ReactiveCommand.Create(ClearMessage);
            ToggleAiModeCommand = ReactiveCommand.Create(() => IsAiEnabled = !IsAiEnabled);

            // Subscribe to voice recognition events
            _voiceService.SpeechRecognized += OnSpeechRecognized;

            // Subscribe to AI service events
            _aiService.OnInferenceProgress += (sender, message) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    StatusMessage = message;
                });
            };

            _aiService.OnError += (sender, ex) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    StatusMessage = $"AI Error: {ex.Message}";
                    _logger.LogError(ex, "AI Service error");
                });
            };

            // Initialize with settings
            _ = InitializeAsync(sessionId);
        }

        private async Task InitializeAsync(string? sessionId)
        {
            try
            {
                // Load selected model from settings
                var settings = await _userSettingsService.GetSettingsAsync();
                if (!string.IsNullOrEmpty(settings.SelectedModelId))
                {
                    _selectedModel = await _aiModelService.GetModelAsync(settings.SelectedModelId);
                }

                // If no model is selected or found, try to find a text model that's downloaded
                if (_selectedModel == null)
                {
                    var models = await _aiModelService.GetAllModelsAsync();
                    _selectedModel = models.FirstOrDefault(m => m.Status == ModelStatus.Downloaded);
                }

                // If we have a session ID, load the existing chat
                if (sessionId != null)
                {
                    await LoadChatHistoryAsync(sessionId);
                }
                else
                {
                    // Add welcome message
                    await AddMessageAsync(new ChatMessage
                    {
                        Content = $"Hello! I'm Nexi. {(_selectedModel != null ? $"I'm using the {_selectedModel.Name} model." : "No AI model is currently selected. Please visit the Models page to download a model.")}",
                        Timestamp = DateTime.Now,
                        IsUser = false
                    });

                    if (_selectedModel == null)
                    {
                        IsAiEnabled = false;
                        StatusMessage = "No AI model selected. Please visit the Models page to download a model.";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing chat");
                StatusMessage = $"Error initializing: {ex.Message}";
            }
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

        public bool IsAiEnabled
        {
            get => _isAiEnabled;
            set => this.RaiseAndSetIfChanged(ref _isAiEnabled, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public bool HasMessageText => !string.IsNullOrWhiteSpace(CurrentMessage);

        public ICommand SendMessageCommand { get; }
        public ICommand ClearMessageCommand { get; }
        public ICommand ToggleAiModeCommand { get; }

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
                StatusMessage = $"Error loading chat: {ex.Message}";
            }
        }

        private async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(CurrentMessage)) return;

            IsProcessing = true;

            try
            {
                string userMessage = CurrentMessage;

                // Add user's message
                await AddMessageAsync(new ChatMessage
                {
                    Content = userMessage,
                    Timestamp = DateTime.Now,
                    IsUser = true
                });

                // Clear input box
                CurrentMessage = string.Empty;

                // First check if it's a command
                if (_commandProcessor.IsCommand(userMessage))
                {
                    string response = _commandProcessor.ProcessCommand(userMessage);

                    await AddMessageAsync(new ChatMessage
                    {
                        Content = response,
                        Timestamp = DateTime.Now,
                        IsUser = false
                    });
                }
                // Otherwise, use AI if enabled
                else if (IsAiEnabled)
                {
                    if (_selectedModel != null && _selectedModel.Status == ModelStatus.Downloaded)
                    {
                        try
                        {
                            StatusMessage = "Generating AI response...";

                            // Convert chat history to format expected by AI service
                            var history = Messages.Take(Messages.Count - 1) // Exclude the message we just added
                                .Select(m => (m.IsUser, m.Content))
                                .ToList();

                            // Create AI options
                            var options = new AIRequestOptions
                            {
                                ModelId = _selectedModel.Id,
                                Temperature = 0.7M,
                                MaxTokens = 1000,
                                SystemPrompt = "You are a helpful AI assistant named Nexi."
                            };

                            // Get AI response
                            var aiResponse = await _aiService.GetCompletionWithHistoryAsync(history, userMessage, options);

                            // Add AI response to chat
                            await AddMessageAsync(new ChatMessage
                            {
                                Content = aiResponse.Text,
                                Timestamp = DateTime.Now,
                                IsUser = false
                            });

                            StatusMessage = "Response generated successfully";
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error generating AI response");

                            await AddMessageAsync(new ChatMessage
                            {
                                Content = $"Sorry, I encountered an error generating a response: {ex.Message}",
                                Timestamp = DateTime.Now,
                                IsUser = false
                            });

                            StatusMessage = $"Error: {ex.Message}";
                        }
                    }
                    else
                    {
                        await AddMessageAsync(new ChatMessage
                        {
                            Content = "I can't generate a response because no AI model is downloaded. Please visit the Models page to download a model.",
                            Timestamp = DateTime.Now,
                            IsUser = false
                        });

                        StatusMessage = "No AI model available";
                    }
                }
                else
                {
                    await AddMessageAsync(new ChatMessage
                    {
                        Content = "AI mode is currently disabled. You can enable it using the toggle button.",
                        Timestamp = DateTime.Now,
                        IsUser = false
                    });
                }

                // Auto-set title if this is the first user message
                if (Title == "New Chat" && Messages.Count >= 2)
                {
                    Title = userMessage.Length > 25 ? userMessage.Substring(0, 22) + "..." : userMessage;
                    await UpdateSessionTitleAsync(_sessionId, Title);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
                StatusMessage = $"Error: {ex.Message}";
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
                CurrentMessage = text;
                await ProcessInputAsync(text);
            });
        }

        private async Task ProcessInputAsync(string input)
        {
            // Add user's message
            await AddMessageAsync(new ChatMessage
            {
                Content = input,
                Timestamp = DateTime.Now,
                IsUser = true
            });

            CurrentMessage = string.Empty;

            // Process message
            string response;
            if (_commandProcessor.IsCommand(input))
            {
                response = _commandProcessor.ProcessCommand(input);

                // Auto-set title if this is the first user message
                if (Title == "New Chat" && Messages.Count <= 3)
                {
                    Title = input.Length > 25 ? input.Substring(0, 22) + "..." : input;
                    await UpdateSessionTitleAsync(_sessionId, Title);
                }
            }
            else
            {
                response = "That's not a command I recognize. Type 'help' to see available commands.";
            }

            // Add response
            await AddMessageAsync(new ChatMessage
            {
                Content = response,
                Timestamp = DateTime.Now,
                IsUser = false
            });
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
                StatusMessage = $"Error saving message: {ex.Message}";
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
                _logger.LogError(ex, "Error updating session title");
                StatusMessage = $"Error updating title: {ex.Message}";
            }
        }

        private void ClearMessage()
        {
            CurrentMessage = string.Empty;
        }

        public override void Dispose()
        {
            _voiceService.SpeechRecognized -= OnSpeechRecognized;

            if (IsVoiceModeEnabled)
            {
                _voiceService.StopListeningAsync().Wait();
            }

            base.Dispose();
        }
    }
}