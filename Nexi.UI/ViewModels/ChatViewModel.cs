using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
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

            // Example message for testing
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

            Messages.Add(new ChatMessage
            {
                Content = CurrentMessage,
                Timestamp = DateTime.Now,
                IsUser = true
            });

            // Clear the input
            CurrentMessage = string.Empty;

            // TODO: Process the message and get AI response
            // This is where you'd integrate with your AI processing
            SimulateResponse();
        }

        private void ClearMessage()
        {
            CurrentMessage = string.Empty;
        }

        // Temporary method to simulate AI response
        private async void SimulateResponse()
        {
            // Simulate typing delay
            await Task.Delay(1000);

            Messages.Add(new ChatMessage
            {
                Content = "I'm an AI assistant. This is a placeholder response.",
                Timestamp = DateTime.Now,
                IsUser = false
            });
        }
    }

    public class ChatMessage
    {
        public required string Content { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsUser { get; set; }
    }
}
