using ReactiveUI;
using System.Reactive;

namespace Nexi.Desktop.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private bool _isDarkTheme;

        public SettingsViewModel()
        {
            SaveCommand = ReactiveCommand.Create(Save);
        }

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set => this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
        }

        public ReactiveCommand<Unit, Unit> SaveCommand { get; }

        private void Save()
        {
            // Save settings logic here
        }
    }
}