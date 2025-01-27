namespace Nexi.Desktop.ViewModels
{
    public class ChatMessageViewModel : ViewModelBase
    {
        public required string Text { get; init; }
        public bool IsAssistant { get; init; }
    }
}