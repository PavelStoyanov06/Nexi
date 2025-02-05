using ReactiveUI;
using System.Windows.Input;
using Nexi.Services.Interfaces;

namespace Nexi.UI.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private bool _isSidebarExpanded = true;
        private double _sidebarWidth;
        private ViewModelBase _currentPage;
        private const double EXPANDED_WIDTH = 250;
        private const double COLLAPSED_WIDTH = 60;
        private readonly ICommandProcessor _commandProcessor;

        public MainViewModel(ICommandProcessor commandProcessor)
        {
            _commandProcessor = commandProcessor;

            // Initialize with ChatView
            _currentPage = new ChatViewModel(_commandProcessor);

            UpdateSidebarWidth();

            // Initialize commands
            ToggleSidebarCommand = ReactiveCommand.Create(() =>
            {
                IsSidebarExpanded = !IsSidebarExpanded;
            });

            NewChatCommand = ReactiveCommand.Create(NavigateToNewChat);
            ChatHistoryCommand = ReactiveCommand.Create(NavigateToChatHistory);
            ModelsCommand = ReactiveCommand.Create(NavigateToModels);
            SettingsCommand = ReactiveCommand.Create(NavigateToSettings);
        }

        public bool IsSidebarExpanded
        {
            get => _isSidebarExpanded;
            set
            {
                this.RaiseAndSetIfChanged(ref _isSidebarExpanded, value);
                UpdateSidebarWidth();
            }
        }

        public double SidebarWidth
        {
            get => _sidebarWidth;
            private set => this.RaiseAndSetIfChanged(ref _sidebarWidth, value);
        }

        public ViewModelBase CurrentPage
        {
            get => _currentPage;
            private set => this.RaiseAndSetIfChanged(ref _currentPage, value);
        }

        private void UpdateSidebarWidth()
        {
            SidebarWidth = IsSidebarExpanded ? EXPANDED_WIDTH : COLLAPSED_WIDTH;
        }

        public ICommand ToggleSidebarCommand { get; }
        public ICommand NewChatCommand { get; }
        public ICommand ChatHistoryCommand { get; }
        public ICommand ModelsCommand { get; }
        public ICommand SettingsCommand { get; }

        private void NavigateToNewChat()
        {
            CurrentPage = new ChatViewModel(_commandProcessor);
        }

        private void NavigateToChatHistory()
        {
            CurrentPage = new ChatHistoryViewModel();
        }

        private void NavigateToModels()
        {
            CurrentPage = new ModelsViewModel();
        }

        private void NavigateToSettings()
        {
            CurrentPage = new SettingsViewModel();
        }
    }
}