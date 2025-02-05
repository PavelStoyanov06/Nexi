using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Nexi.Services.Interfaces;
using Nexi.UI.Models;

namespace Nexi.UI.ViewModels
{
    public class ChatViewModel : ViewModelBase
    {
        private readonly ICommandProcessor _commandProcessor;
        private string _currentMessage = string.Empty;
        private bool _isVoiceModeEnabled;
        private ObservableCollection<ChatMessage> _messages;

        public ChatViewModel(ICommandProcessor commandProcessor)
        {
            _commandProcessor = commandProcessor;
            Messages = new ObservableCollection<ChatMessage>();

            // Initialize commands
            SendMessageCommand = ReactiveCommand.Create(SendMessage);
            ClearMessageCommand = ReactiveCommand.Create(ClearMessage);

            // Add welcome message
            Messages.Add(new ChatMessage
            {
                Content = "Hello! I'm Nexi. You can type 'help' to see available commands.",
                Timestamp = DateTime.Now,
                IsUser = false
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

        public bool HasMessageText => !string.IsNullOrWhiteSpace(CurrentMessage);

        public ICommand SendMessageCommand { get; }
        public ICommand ClearMessageCommand { get; }

        private void SendMessage()
        {
            if (string.IsNullOrWhiteSpace(CurrentMessage)) return;

            // Add user's message
            Messages.Add(new ChatMessage
            {
                Content = CurrentMessage,
                Timestamp = DateTime.Now,
                IsUser = true
            });

            // Store message and clear input
            string userMessage = CurrentMessage;
            CurrentMessage = string.Empty;

            // Process message
            string response;
            if (_commandProcessor.IsCommand(userMessage))
            {
                response = _commandProcessor.ProcessCommand(userMessage);
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
    }
}