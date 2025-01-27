using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive;

namespace Nexi.Desktop.ViewModels
{
    public class ChatViewModel : ViewModelBase
    {
        private bool _isRecording;
        private string _inputText = string.Empty;

        public ChatViewModel()
        {
            Messages = new ObservableCollection<ChatMessageViewModel>();
            ToggleRecordingCommand = ReactiveCommand.Create(ToggleRecording);
            SendMessageCommand = ReactiveCommand.Create(SendMessage);

            // Add initial message
            Messages.Add(new ChatMessageViewModel
            {
                Text = "Hello! I'm Nexi, your AI assistant. How can I help you today?",
                IsAssistant = true
            });
        }

        public bool IsRecording
        {
            get => _isRecording;
            set => this.RaiseAndSetIfChanged(ref _isRecording, value);
        }

        public string InputText
        {
            get => _inputText;
            set => this.RaiseAndSetIfChanged(ref _inputText, value);
        }

        public ObservableCollection<ChatMessageViewModel> Messages { get; }

        public ReactiveCommand<Unit, Unit> ToggleRecordingCommand { get; }
        public ReactiveCommand<Unit, Unit> SendMessageCommand { get; }

        private void ToggleRecording()
        {
            IsRecording = !IsRecording;
        }

        private void SendMessage()
        {
            if (string.IsNullOrWhiteSpace(InputText))
                return;

            Messages.Add(new ChatMessageViewModel
            {
                Text = InputText,
                IsAssistant = false
            });
            InputText = string.Empty;
        }
    }
}
