using Avalonia;
using ReactiveUI;
using System.Windows.Input;

namespace Nexi.UI.ViewModels;

public class MainViewModel : ViewModelBase
{
    private bool _isSidebarExpanded = true;
    private double _sidebarWidth;
    private ViewModelBase _currentPage;
    private const double EXPANDED_WIDTH = 250;
    private const double COLLAPSED_WIDTH = 60;

    public MainViewModel()
    {
        UpdateSidebarWidth();
        UpdateIconMargin();

        // Initialize commands
        ToggleSidebarCommand = ReactiveCommand.Create(() =>
        {
            IsSidebarExpanded = !IsSidebarExpanded;
            UpdateIconMargin();
        });

        HomeCommand = ReactiveCommand.Create(NavigateToHome);
        SettingsCommand = ReactiveCommand.Create(NavigateToSettings);
        VoiceCommand = ReactiveCommand.Create(NavigateToVoice);
        AIModelsCommand = ReactiveCommand.Create(NavigateToAIModels);
        AboutCommand = ReactiveCommand.Create(NavigateToAbout);

        // Set initial page
        NavigateToHome();
    }

    public Thickness IconMargin => IsSidebarExpanded ? new Thickness(0) : new Thickness(8, 0, 0, 0);

    private void UpdateIconMargin()
    {
        this.RaisePropertyChanged(nameof(IconMargin));
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

    // Commands
    public ICommand ToggleSidebarCommand { get; }
    public ICommand HomeCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand VoiceCommand { get; }
    public ICommand AIModelsCommand { get; }
    public ICommand AboutCommand { get; }

    // Navigation Methods
    private void NavigateToHome()
    {
        CurrentPage = new HomeViewModel();
    }

    private void NavigateToSettings()
    {
        CurrentPage = new SettingsViewModel();
    }

    private void NavigateToVoice()
    {
        CurrentPage = new VoiceCommandsViewModel();
    }

    private void NavigateToAIModels()
    {
        CurrentPage = new AIModelsViewModel();
    }

    private void NavigateToAbout()
    {
        CurrentPage = new AboutViewModel();
    }
}
