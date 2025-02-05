using Avalonia;
using ReactiveUI;
using System;
using System.Collections.Generic;

namespace Nexi.UI.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private int _selectedModelIndex;
        private bool _useGPU;
        private int _selectedInputDeviceIndex;
        private double _inputSensitivity = 50;
        private ThemeMode _selectedTheme = ThemeMode.System;
        private bool _useSystemAccent = true;

        public SettingsViewModel()
        {
            // Subscribe to theme changes
            this.WhenAnyValue(x => x.SelectedTheme)
                .Subscribe(UpdateTheme);

            this.WhenAnyValue(x => x.UseSystemAccent)
                .Subscribe(UpdateAccentColor);
        }

        public IEnumerable<ThemeMode> ThemeOptions => Enum.GetValues<ThemeMode>();

        public int SelectedModelIndex
        {
            get => _selectedModelIndex;
            set => this.RaiseAndSetIfChanged(ref _selectedModelIndex, value);
        }

        public bool UseGPU
        {
            get => _useGPU;
            set => this.RaiseAndSetIfChanged(ref _useGPU, value);
        }

        public int SelectedInputDeviceIndex
        {
            get => _selectedInputDeviceIndex;
            set => this.RaiseAndSetIfChanged(ref _selectedInputDeviceIndex, value);
        }

        public double InputSensitivity
        {
            get => _inputSensitivity;
            set => this.RaiseAndSetIfChanged(ref _inputSensitivity, value);
        }

        public ThemeMode SelectedTheme
        {
            get => _selectedTheme;
            set => this.RaiseAndSetIfChanged(ref _selectedTheme, value);
        }

        public bool UseSystemAccent
        {
            get => _useSystemAccent;
            set => this.RaiseAndSetIfChanged(ref _useSystemAccent, value);
        }

        private void UpdateTheme(ThemeMode mode)
        {
            App.UpdateTheme(mode);
        }

        private void UpdateAccentColor(bool useSystem)
        {
            App.UpdateAccentColor(useSystem);
        }
    }

    public enum ThemeMode
    {
        System,
        Light,
        Dark
    }
}