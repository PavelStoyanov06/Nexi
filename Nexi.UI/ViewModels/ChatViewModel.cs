using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Nexi.Services.Interfaces;
using Nexi.UI.Models;
using Avalonia.Threading;
using Avalonia;

namespace Nexi.UI.ViewModels
{
    public class ChatViewModel : ViewModelBase
    {
        private readonly ICommandProcessor _commandProcessor;
        private readonly IVoiceService _voiceService;
        private string _currentMessage = string.Empty;
        private bool _isVoiceModeEnabled;
        private ObservableCollection<ChatMessage> _messages;
        private bool _isProcessing;

        public ChatViewModel(ICommandProcessor commandProcessor, IVoiceService voiceService)
        {
            _commandProcessor = commandProcessor;
            _voiceService = voiceService;
            Messages = new ObservableCollection<ChatMessage>();

            // Initialize commands
            SendMessageCommand = ReactiveCommand.Create(SendMessage);
            ClearMessageCommand = ReactiveCommand.Create(ClearMessage);

            // Subscribe to voice recognition events
            _voiceService.SpeechRecognized += OnSpeechRecognized;

            // Add welcome message
            Messages.Add(new ChatMessage
            {
                Content = "Hello! I'm Nexi. You can type 'help' to see available commands, or use the microphone button for voice commands.",
                Timestamp = DateTime.Now,
                IsUser = false
            });

            // Subscribe to voice mode changes
            this.WhenAnyValue(x => x.IsVoiceModeEnabled)
                .Subscribe(async isEnabled =>
                {
                    if (isEnabled)
                    {
                        await _voiceService.StartListeningAsync();
                        Messages.Add(new ChatMessage
                        {
                            Content = "Voice mode enabled. Speak your commands.",
                            Timestamp = DateTime.Now,
                            IsUser = false
                        });
                    }
                    else
                    {
                        await _voiceService.StopListeningAsync();
                        Messages.Add(new ChatMessage
                        {
                            Content = "Voice mode disabled.",
                            Timestamp = DateTime.Now,
                            IsUser = false
                        });
                    }
                });
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

        private void SendMessage()
        {
            if (string.IsNullOrWhiteSpace(CurrentMessage)) return;

            ProcessInput(CurrentMessage);
            CurrentMessage = string.Empty;
        }

        private void OnSpeechRecognized(object? sender, string text)
        {
            Dispatcher.UIThread.Post(() =>
            {
                ProcessInput(text);
            });
        }

        private void ProcessInput(string input)
        {
            // Add user's message
            Messages.Add(new ChatMessage
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
            }
            else
            {
                response = "That's not a command I recognize. Type 'help' to see available commands.";
            }

            // Add response
            Messages.Add(new ChatMessage
            {
                Content = response,
                Timestamp = DateTime.Now,
                IsUser = false
            });
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
