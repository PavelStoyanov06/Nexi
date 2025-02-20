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

namespace Nexi.UI.ViewModels
{
    public class ChatViewModel : ViewModelBase
    {
        private readonly ICommandProcessor _commandProcessor;
        private readonly IVoiceService _voiceService;
        private readonly IChatStorageService _chatStorage;
        private readonly ILogger<ChatViewModel> _logger;
        private string _currentMessage = string.Empty;
        private bool _isVoiceModeEnabled;
        private ObservableCollection<ChatMessage> _messages;
        private bool _isProcessing;
        private string _sessionId;
        private string _title;

        public ChatViewModel(
            ICommandProcessor commandProcessor,
            IVoiceService voiceService,
            IChatStorageService chatStorage,
            ILogger<ChatViewModel> logger,
            string? sessionId = null)
        {
            _commandProcessor = commandProcessor;
            _voiceService = voiceService;
            _chatStorage = chatStorage;
            _logger = logger;
            _sessionId = sessionId ?? Guid.NewGuid().ToString();
            _title = "New Chat";
            Messages = new ObservableCollection<ChatMessage>();

            // Initialize commands
            SendMessageCommand = ReactiveCommand.CreateFromTask(SendMessageAsync);
            ClearMessageCommand = ReactiveCommand.Create(ClearMessage);

            // Subscribe to voice recognition events
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
            if (string.IsNullOrWhiteSpace(CurrentMessage)) return;

            await ProcessInputAsync(CurrentMessage);
            CurrentMessage = string.Empty;
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
            // Add user's message
            await AddMessageAsync(new ChatMessage
            {
                Content = input,
                Timestamp = DateTime.Now,
                IsUser = true
            });

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