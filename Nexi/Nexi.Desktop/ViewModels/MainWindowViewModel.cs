using Avalonia.Controls;
using Avalonia.Media;
using ReactiveUI;
using System.Reactive;

namespace Nexi.Desktop.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private bool _isSidebarExpanded = true;
        private bool _isDarkTheme;
        private ViewModelBase? _currentView;

        public MainWindowViewModel()
        {
            ToggleSidebarCommand = ReactiveCommand.Create(ToggleSidebar);
            ToggleThemeCommand = ReactiveCommand.Create(ToggleTheme);
            NavigateToCommand = ReactiveCommand.Create<string>(NavigateTo);

            // Set initial view
            CurrentView = new ChatViewModel();
        }

        public bool IsSidebarExpanded
        {
            get => _isSidebarExpanded;
            set => this.RaiseAndSetIfChanged(ref _isSidebarExpanded, value);
        }

        public double SidebarWidth => IsSidebarExpanded ? 250 : 48;

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set => this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
        }

        public ViewModelBase? CurrentView
        {
            get => _currentView;
            set => this.RaiseAndSetIfChanged(ref _currentView, value);
        }

        public StreamGeometry ThemeIcon => IsDarkTheme ?
            (StreamGeometry)App.Current.Resources["SunIcon"] :
            (StreamGeometry)App.Current.Resources["MoonIcon"];

        public ReactiveCommand<Unit, Unit> ToggleSidebarCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleThemeCommand { get; }
        public ReactiveCommand<string, Unit> NavigateToCommand { get; }

        private void ToggleSidebar()
        {
            IsSidebarExpanded = !IsSidebarExpanded;
            this.RaisePropertyChanged(nameof(SidebarWidth));
        }

        private void ToggleTheme()
        {
            IsDarkTheme = !IsDarkTheme;
            this.RaisePropertyChanged(nameof(ThemeIcon));
            // Theme switching logic here
        }

        private void NavigateTo(string viewName)
        {
            CurrentView = viewName switch
            {
                "chat" => new ChatViewModel(),
                "notifications" => new NotificationsViewModel(),
                "settings" => new SettingsViewModel(),
                _ => CurrentView
            };
        }
    }
}