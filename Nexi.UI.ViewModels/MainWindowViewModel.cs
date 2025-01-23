using System.Collections.ObjectModel;
using System.Windows.Input;
using ReactiveUI;

namespace Nexi.UI.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private string _inputText = string.Empty;
        private bool _isVoiceActive;
        private bool _isSidebarExpanded = true;
        private ObservableCollection<ChatMessage> _chatMessages;

        public MainWindowViewModel()
        {
            _chatMessages = new ObservableCollection<ChatMessage>();
            SendMessageCommand = ReactiveCommand.Create(SendMessage);
            ToggleVoiceCommand = ReactiveCommand.Create(ToggleVoice);
            OpenSettingsCommand = ReactiveCommand.Create(OpenSettings);
            ToggleSidebarCommand = ReactiveCommand.Create(ToggleSidebar);
        }

        public string InputText
        {
            get => _inputText;
            set => this.RaiseAndSetIfChanged(ref _inputText, value);
        }

        public bool IsVoiceActive
        {
            get => _isVoiceActive;
            set => this.RaiseAndSetIfChanged(ref _isVoiceActive, value);
        }

        public bool IsSidebarExpanded
        {
            get => _isSidebarExpanded;
            set => this.RaiseAndSetIfChanged(ref _isSidebarExpanded, value);
        }

        public double SidebarWidth => IsSidebarExpanded ? 200 : 60;

        public ObservableCollection<ChatMessage> ChatMessages
        {
            get => _chatMessages;
            private set => this.RaiseAndSetIfChanged(ref _chatMessages, value);
        }

        public ICommand SendMessageCommand { get; }
        public ICommand ToggleVoiceCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand ToggleSidebarCommand { get; }

        private void SendMessage()
        {
            if (string.IsNullOrWhiteSpace(InputText)) return;

            ChatMessages.Add(new ChatMessage { Content = InputText, IsUser = true });
            // TODO: Process message and get AI response
            InputText = string.Empty;
        }

        private void ToggleVoice()
        {
            IsVoiceActive = !IsVoiceActive;
            // TODO: Implement voice recognition
        }

        private void OpenSettings()
        {
            // TODO: Implement settings dialog
        }

        private void ToggleSidebar()
        {
            IsSidebarExpanded = !IsSidebarExpanded;
            this.RaisePropertyChanged(nameof(SidebarWidth));
        }
    }

    public class ChatMessage
    {
        public required string Content { get; set; }
        public bool IsUser { get; set; }
    }
}