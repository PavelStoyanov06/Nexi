// Nexi.UI/ViewModels/ChatViewModel.cs
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Nexi.UI.ViewModels
{
    public class ChatViewModel : ViewModelBase
    {
        private string _currentMessage = string.Empty;
        private bool _isVoiceModeEnabled;
        private ObservableCollection<ChatMessage> _messages;

        public ChatViewModel()
        {
            Messages = new ObservableCollection<ChatMessage>();

            // Initialize commands
            SendMessageCommand = ReactiveCommand.Create(SendMessage);
            ClearMessageCommand = ReactiveCommand.Create(ClearMessage);

            // Add welcome message
            Messages.Add(new ChatMessage
            {
                Content = "Hello! How can I help you today?",
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

            // Store the message before clearing input
            string userMessage = CurrentMessage;

            // Clear the input
            CurrentMessage = string.Empty;

            // Simple echo response
            Messages.Add(new ChatMessage
            {
                Content = $"You said: {userMessage}",
                Timestamp = DateTime.Now,
                IsUser = false
            });
        }

        private void ClearMessage()
        {
            CurrentMessage = string.Empty;
        }
    }

    public class ChatMessage
    {
        public required string Content { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsUser { get; set; }
    }
}