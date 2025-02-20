using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;
using Avalonia.Styling;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace Nexi.UI.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly IUserSettingsService _userSettingsService;
        private readonly IAIModelService _aiModelService;
        private readonly ILogger<SettingsViewModel> _logger;

        private int _selectedModelIndex;
        private bool _useGPU;
        private int _selectedInputDeviceIndex;
        private double _inputSensitivity = 50;
        private ThemeMode _selectedTheme = ThemeMode.System;
        private bool _useSystemAccent = true;
        private string? _selectedModelId;
        private ObservableCollection<AIModelData> _availableModels;
        private bool _isLoading = false;

        public SettingsViewModel(
            IUserSettingsService userSettingsService,
            IAIModelService aiModelService,
            ILogger<SettingsViewModel> logger)
        {
            _userSettingsService = userSettingsService;
            _aiModelService = aiModelService;
            _logger = logger;

            _availableModels = new ObservableCollection<AIModelData>();

            // Initialize commands
            SaveSettingsCommand = ReactiveCommand.CreateFromTask(SaveSettingsAsync);

            // Subscribe to property changes
            this.WhenAnyValue(x => x.SelectedTheme)
                .Skip(1) // Skip initial value
                .Subscribe(async theme => {
                    UpdateTheme(theme);
                    await _userSettingsService.UpdateThemeAsync(theme);
                });

            this.WhenAnyValue(x => x.UseSystemAccent)
                .Skip(1)
                .Subscribe(async useSystem => {
                    UpdateAccentColor(useSystem);
                    await _userSettingsService.UpdateAccentColorAsync(useSystem);
                });

            this.WhenAnyValue(x => x.UseGPU)
                .Skip(1)
                .Subscribe(async useGPU => {
                    await _userSettingsService.UpdateUseGPUAsync(useGPU);
                });

            this.WhenAnyValue(x => x.InputSensitivity)
                .Throttle(TimeSpan.FromMilliseconds(500))
                .Skip(1)
                .Subscribe(async sensitivity => {
                    await _userSettingsService.UpdateVoiceSettingsAsync(_selectedInputDevice, (int)sensitivity);
                });

            // Load settings
            _ = LoadSettingsAsync();
        }

        public ObservableCollection<AIModelData> AvailableModels
        {
            get => _availableModels;
            set => this.RaiseAndSetIfChanged(ref _availableModels, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => this.RaiseAndSetIfChanged(ref _isLoading, value);
        }

        public int SelectedModelIndex
        {
            get => _selectedModelIndex;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedModelIndex, value);
                if (value >= 0 && value < AvailableModels.Count)
                {
                    _selectedModelId = AvailableModels[value].Id;
                    _ = _userSettingsService.UpdateSelectedModelAsync(_selectedModelId);
                }
            }
        }

        public bool UseGPU
        {
            get => _useGPU;
            set => this.RaiseAndSetIfChanged(ref _useGPU, value);
        }

        private string? _selectedInputDevice;
        public int SelectedInputDeviceIndex
        {
            get => _selectedInputDeviceIndex;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedInputDeviceIndex, value);
                // Map index to actual device name
                _selectedInputDevice = value == 0 ? "Default" : "Headset";
                _ = _userSettingsService.UpdateVoiceSettingsAsync(_selectedInputDevice, (int)InputSensitivity);
            }
        }

        public double InputSensitivity
        {
            get => _inputSensitivity;
            set => this.RaiseAndSetIfChanged(ref _inputSensitivity, value);
        }

        // Theme properties
        public IEnumerable<ThemeMode> ThemeOptions => Enum.GetValues<ThemeMode>();

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

        public ICommand SaveSettingsCommand { get; }

        private async Task LoadSettingsAsync()
        {
            try
            {
                IsLoading = true;

                // Load user settings
                var settings = await _userSettingsService.GetSettingsAsync();
                _selectedTheme = settings.SelectedTheme;
                _useSystemAccent = settings.UseSystemAccent;
                _useGPU = settings.UseGPU;
                _inputSensitivity = settings.InputSensitivity;
                _selectedModelId = settings.SelectedModelId;
                _selectedInputDevice = settings.SelectedInputDevice;

                if (settings.SelectedInputDevice == "Default")
                    _selectedInputDeviceIndex = 0;
                else if (settings.SelectedInputDevice == "Headset")
                    _selectedInputDeviceIndex = 1;

                // Load AI models
                var models = await _aiModelService.GetAllModelsAsync();

                AvailableModels.Clear();
                foreach (var model in models)
                {
                    AvailableModels.Add(model);
                }

                // Set selected model
                if (!string.IsNullOrEmpty(_selectedModelId))
                {
                    for (int i = 0; i < AvailableModels.Count; i++)
                    {
                        if (AvailableModels[i].Id == _selectedModelId)
                        {
                            SelectedModelIndex = i;
                            break;
                        }
                    }
                }

                // Update UI with loaded settings
                this.RaisePropertyChanged(nameof(SelectedTheme));
                this.RaisePropertyChanged(nameof(UseSystemAccent));
                this.RaisePropertyChanged(nameof(UseGPU));
                this.RaisePropertyChanged(nameof(InputSensitivity));
                this.RaisePropertyChanged(nameof(SelectedInputDeviceIndex));
                this.RaisePropertyChanged(nameof(SelectedModelIndex));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading settings");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task SaveSettingsAsync()
        {
            try
            {
                var settings = new UserSettings
                {
                    SelectedModelId = _selectedModelId,
                    UseGPU = UseGPU,
                    SelectedInputDevice = _selectedInputDevice,
                    InputSensitivity = (int)InputSensitivity,
                    SelectedTheme = SelectedTheme,
                    UseSystemAccent = UseSystemAccent
                };

                await _userSettingsService.UpdateSettingsAsync(settings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving settings");
            }
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
}