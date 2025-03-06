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
        private AIModelData? _selectedModel;
        private bool _useGPU;
        private int _selectedInputDeviceIndex;
        private double _inputSensitivity = 50;
        private ThemeMode _selectedTheme = ThemeMode.System;
        private bool _useSystemAccent = true;
        private string? _selectedModelId;
        private ObservableCollection<AIModelData> _availableModels;
        private bool _isLoading = false;
        private int _contextSize = 1024;
        private int _gpuLayerCount = 5;

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

            // Subscribe to property changes with debounce to avoid concurrent operations
            this.WhenAnyValue(x => x.SelectedTheme)
                .Skip(1) // Skip initial value
                .Throttle(TimeSpan.FromMilliseconds(300)) // Add debounce
                .ObserveOn(RxApp.MainThreadScheduler) // Ensure we're on the UI thread
                .Subscribe(async theme => {
                    UpdateTheme(theme);
                    try {
                        await _userSettingsService.UpdateThemeAsync(theme);
                    } catch (Exception ex) {
                        _logger.LogError(ex, "Error updating theme");
                    }
                });

            this.WhenAnyValue(x => x.UseSystemAccent)
                .Skip(1)
                .Throttle(TimeSpan.FromMilliseconds(300))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(async useSystem => {
                    UpdateAccentColor(useSystem);
                    try {
                        await _userSettingsService.UpdateAccentColorAsync(useSystem);
                    } catch (Exception ex) {
                        _logger.LogError(ex, "Error updating accent color");
                    }
                });

            this.WhenAnyValue(x => x.UseGPU)
                .Skip(1)
                .Throttle(TimeSpan.FromMilliseconds(300))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(async useGPU => {
                    try {
                        await _userSettingsService.UpdateUseGPUAsync(useGPU);
                    } catch (Exception ex) {
                        _logger.LogError(ex, "Error updating GPU setting");
                    }
                });

            this.WhenAnyValue(x => x.InputSensitivity)
                .Throttle(TimeSpan.FromMilliseconds(500))
                .Skip(1)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(async sensitivity => {
                    try {
                        await _userSettingsService.UpdateVoiceSettingsAsync(_selectedInputDevice, (int)sensitivity);
                    } catch (Exception ex) {
                        _logger.LogError(ex, "Error updating input sensitivity");
                    }
                });

            this.WhenAnyValue(x => x.ContextSize)
                .Throttle(TimeSpan.FromMilliseconds(500))
                .Skip(1)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(async contextSize => {
                    try {
                        await _userSettingsService.UpdateLlamaSharpSettingsAsync(contextSize, GpuLayerCount);
                        _logger.LogInformation($"Updated context size to {contextSize}");
                    } catch (Exception ex) {
                        _logger.LogError(ex, "Error updating context size");
                    }
                });

            this.WhenAnyValue(x => x.GpuLayerCount)
                .Throttle(TimeSpan.FromMilliseconds(500))
                .Skip(1)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(async gpuLayerCount => {
                    try {
                        await _userSettingsService.UpdateLlamaSharpSettingsAsync(ContextSize, gpuLayerCount);
                        _logger.LogInformation($"Updated GPU layer count to {gpuLayerCount}");
                    } catch (Exception ex) {
                        _logger.LogError(ex, "Error updating GPU layer count");
                    }
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

        public AIModelData? SelectedModel
        {
            get => _selectedModel;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedModel, value);
                if (value != null)
                {
                    _selectedModelId = value.Id;
                    _logger.LogInformation($"Model selection changed to {_selectedModelId}");
                    _ = UpdateSelectedModelAsync(_selectedModelId);
                }
            }
        }

        private async Task UpdateSelectedModelAsync(string modelId)
        {
            try
            {
                var settings = await _userSettingsService.UpdateSelectedModelAsync(modelId);
                if (settings != null && settings.SelectedModelId == modelId)
                {
                    _logger.LogInformation($"Successfully updated selected model to {modelId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating selected model to {modelId}");
            }
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

        public int ContextSize
        {
            get => _contextSize;
            set => this.RaiseAndSetIfChanged(ref _contextSize, value);
        }

        public int GpuLayerCount
        {
            get => _gpuLayerCount;
            set => this.RaiseAndSetIfChanged(ref _gpuLayerCount, value);
        }

        public ICommand SaveSettingsCommand { get; }

        private async Task LoadSettingsAsync()
        {
            try
            {
                IsLoading = true;
                
                // Load user settings
                var settings = await _userSettingsService.GetSettingsAsync();
                _selectedModelId = settings.SelectedModelId;
                _useGPU = settings.UseGPU;
                _selectedInputDevice = settings.SelectedInputDevice;
                _inputSensitivity = settings.InputSensitivity;
                _selectedTheme = settings.SelectedTheme;
                _useSystemAccent = settings.UseSystemAccent;
                _contextSize = settings.ContextSize;
                _gpuLayerCount = settings.GpuLayerCount;
                
                // Set input device index
                if (settings.SelectedInputDevice == "Default")
                    _selectedInputDeviceIndex = 0;
                else if (settings.SelectedInputDevice == "Headset")
                    _selectedInputDeviceIndex = 1;
                
                // Refresh models (this will also set the selected model)
                await RefreshModelsAsync();
                
                // Update UI properties
                this.RaisePropertyChanged(nameof(UseGPU));
                this.RaisePropertyChanged(nameof(SelectedInputDeviceIndex));
                this.RaisePropertyChanged(nameof(InputSensitivity));
                this.RaisePropertyChanged(nameof(SelectedTheme));
                this.RaisePropertyChanged(nameof(UseSystemAccent));
                this.RaisePropertyChanged(nameof(ContextSize));
                this.RaisePropertyChanged(nameof(GpuLayerCount));
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
                    UseSystemAccent = UseSystemAccent,
                    ContextSize = ContextSize,
                    GpuLayerCount = GpuLayerCount
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

        public async Task RefreshModelsAsync()
        {
            try
            {
                IsLoading = true;
                
                // Load AI models
                var models = await _aiModelService.GetAllModelsAsync();
                
                AvailableModels.Clear();
                foreach (var model in models)
                {
                    // Only add models that are downloaded
                    if (model.Status == ModelStatus.Downloaded)
                    {
                        AvailableModels.Add(model);
                        _logger.LogInformation($"Added downloaded model to available models: {model.Id}");
                    }
                }
                
                // Get current settings to find the selected model
                var settings = await _userSettingsService.GetSettingsAsync();
                _selectedModelId = settings.SelectedModelId;
                
                // Set selected model if it exists and is downloaded
                if (!string.IsNullOrEmpty(_selectedModelId))
                {
                    foreach (var model in AvailableModels)
                    {
                        if (model.Id == _selectedModelId)
                        {
                            _selectedModel = model;
                            this.RaisePropertyChanged(nameof(SelectedModel));
                            _logger.LogInformation($"Selected model {_selectedModelId} found and set as current");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing models");
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}