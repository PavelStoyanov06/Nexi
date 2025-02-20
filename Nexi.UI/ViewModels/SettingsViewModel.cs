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
using Avalonia.Threading;
using System.Threading;
using Nexi.Services;
using System.Linq;

namespace Nexi.UI.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly IUserSettingsService _userSettingsService;
        private readonly IChatStorageService _chatStorageService;
        private readonly IAIModelService _aiModelService;
        private readonly IVoiceService _voiceService;
        private readonly ILogger<SettingsViewModel> _logger;

        private int _selectedModelIndex;
        private bool _useGPU;
        private int _selectedInputDeviceIndex;
        private double _inputSensitivity = 50;
        private ThemeMode _selectedTheme = ThemeMode.System;
        private bool _useSystemAccent = true;
        private string? _selectedModelId;
        private ObservableCollection<AIModelData> _availableModels;
        private ObservableCollection<string> _inputDevices;
        private bool _isLoading = false;
        private Timer? _sensitivityDebounceTimer;
        private string _voiceTestStatus = "Not tested";
        private string _diagnosticInfo = "Loading...";

        public SettingsViewModel(
            IUserSettingsService userSettingsService,
            IAIModelService aiModelService,
            IVoiceService voiceService,
            ILogger<SettingsViewModel> logger,
            IChatStorageService chatStorageService)
        {
            _userSettingsService = userSettingsService;
            _aiModelService = aiModelService;
            _voiceService = voiceService;
            _logger = logger;
            _chatStorageService = chatStorageService;

            _availableModels = new ObservableCollection<AIModelData>();
            _inputDevices = new ObservableCollection<string>();

            // Initialize commands
            SaveSettingsCommand = ReactiveCommand.CreateFromTask(SaveSettingsAsync);
            TestVoiceCommand = ReactiveCommand.CreateFromTask(TestVoiceSettingsAsync);
            VerifyDatabaseCommand = ReactiveCommand.CreateFromTask(VerifyDatabaseAsync);

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

            // Load settings
            _ = LoadSettingsAsync();
        }

        public ObservableCollection<AIModelData> AvailableModels
        {
            get => _availableModels;
            set => this.RaiseAndSetIfChanged(ref _availableModels, value);
        }

        public ObservableCollection<string> InputDevices
        {
            get => _inputDevices;
            set => this.RaiseAndSetIfChanged(ref _inputDevices, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => this.RaiseAndSetIfChanged(ref _isLoading, value);
        }

        public string VoiceTestStatus
        {
            get => _voiceTestStatus;
            set => this.RaiseAndSetIfChanged(ref _voiceTestStatus, value);
        }

        public string DiagnosticInfo
        {
            get => _diagnosticInfo;
            private set => this.RaiseAndSetIfChanged(ref _diagnosticInfo, value);
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
                if (value >= 0 && value < InputDevices.Count)
                {
                    _selectedInputDevice = InputDevices[value];
                    // Apply the change to voice service
                    _ = ApplyVoiceSettingsAsync();
                }
            }
        }

        public double InputSensitivity
        {
            get => _inputSensitivity;
            set
            {
                this.RaiseAndSetIfChanged(ref _inputSensitivity, value);
                // Debounce the sensitivity changes
                _sensitivityDebounceTimer?.Dispose();
                _sensitivityDebounceTimer = new Timer(_ =>
                {
                    Dispatcher.UIThread.Post(() => ApplyVoiceSettingsAsync());
                }, null, 500, Timeout.Infinite);
            }
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
        public ICommand TestVoiceCommand { get; }
        public ICommand VerifyDatabaseCommand { get; }

        private async Task LoadVoiceDevicesAsync()
        {
            try
            {
                var devices = _voiceService.GetAvailableInputDevices();
                InputDevices.Clear();

                foreach (var device in devices)
                {
                    InputDevices.Add(device);
                }

                // If the selected device exists in the list, select it
                if (!string.IsNullOrEmpty(_selectedInputDevice))
                {
                    for (int i = 0; i < InputDevices.Count; i++)
                    {
                        if (InputDevices[i] == _selectedInputDevice)
                        {
                            SelectedInputDeviceIndex = i;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading input devices");
            }
        }

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

                // Load voice devices
                await LoadVoiceDevicesAsync();

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

                // Add diagnostic information
                var allSessions = await _chatStorageService.GetAllSessionsAsync();

                DiagnosticInfo = $"Database Status:\n" +
                    $"• Settings Record: Found\n" +
                    $"• Chat Sessions: {allSessions.Count()} stored\n" +
                    $"• AI Models: {models.Count()} configured\n" +
                    $"• Current Theme: {_selectedTheme}\n" +
                    $"• Selected Device: {_selectedInputDevice ?? "None"}\n" +
                    $"• Sensitivity: {_inputSensitivity}";

                // Update UI with loaded settings
                this.RaisePropertyChanged(nameof(SelectedTheme));
                this.RaisePropertyChanged(nameof(UseSystemAccent));
                this.RaisePropertyChanged(nameof(UseGPU));
                this.RaisePropertyChanged(nameof(InputSensitivity));
                this.RaisePropertyChanged(nameof(SelectedInputDeviceIndex));
                this.RaisePropertyChanged(nameof(SelectedModelIndex));
                this.RaisePropertyChanged(nameof(InputDevices));
                this.RaisePropertyChanged(nameof(DiagnosticInfo));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading settings");
                DiagnosticInfo = $"Error: {ex.Message}";
                this.RaisePropertyChanged(nameof(DiagnosticInfo));
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ApplyVoiceSettingsAsync()
        {
            try
            {
                if (!string.IsNullOrEmpty(_selectedInputDevice))
                {
                    // Update voice service with new settings
                    await _voiceService.UpdateInputDeviceAsync(_selectedInputDevice, (int)_inputSensitivity);

                    // Save to database
                    await _userSettingsService.UpdateVoiceSettingsAsync(_selectedInputDevice, (int)_inputSensitivity);

                    _logger.LogInformation("Applied voice settings: Device={Device}, Sensitivity={Sensitivity}",
                        _selectedInputDevice, (int)_inputSensitivity);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying voice settings");
            }
        }

        private async Task TestVoiceSettingsAsync()
        {
            try
            {
                VoiceTestStatus = "Testing voice recognition...";

                // Test if service is properly configured
                if (_voiceService.IsListening)
                {
                    await _voiceService.StopListeningAsync();
                    VoiceTestStatus = "Stopped listening to restart with new settings...";
                    await Task.Delay(500);
                }

                // Start listening
                await _voiceService.StartListeningAsync();
                VoiceTestStatus = "Listening for 5 seconds... Say something!";

                // Set up a temporary event handler for the test
                EventHandler<string> tempHandler = (s, text) => {
                    Dispatcher.UIThread.Post(() => {
                        VoiceTestStatus = $"Recognized: \"{text}\" (Settings working!)";
                    });
                };

                _voiceService.SpeechRecognized += tempHandler;

                // Listen for a few seconds
                await Task.Delay(5000);

                // Clean up
                _voiceService.SpeechRecognized -= tempHandler;
                await _voiceService.StopListeningAsync();

                if (VoiceTestStatus.StartsWith("Listening"))
                {
                    VoiceTestStatus = "No speech detected in 5 seconds. Try adjusting sensitivity or check your microphone.";
                }
            }
            catch (Exception ex)
            {
                VoiceTestStatus = $"Error: {ex.Message}";
                _logger.LogError(ex, "Error during voice test");
            }
        }

        private async Task VerifyDatabaseAsync()
        {
            try
            {
                DiagnosticInfo = "Testing database connectivity...";
                this.RaisePropertyChanged(nameof(DiagnosticInfo));

                // Test settings retrieval
                var settings = await _userSettingsService.GetSettingsAsync();

                // Test chat storage
                var sessions = await _chatStorageService.GetAllSessionsAsync();

                // Test model listing
                var models = await _aiModelService.GetAllModelsAsync();

                // Create a test session and message
                var testSession = await _chatStorageService.CreateSessionAsync("Test Session");
                await _chatStorageService.AddMessageAsync(testSession.Id, new Nexi.Data.Models.ChatMessageData
                {
                    Content = "Test message",
                    IsUser = true,
                    Timestamp = DateTime.Now
                });

                // Retrieve and verify
                var retrievedSession = await _chatStorageService.GetSessionAsync(testSession.Id);
                var success = retrievedSession != null && retrievedSession.Messages.Count > 0;

                // Clean up test data
                await _chatStorageService.DeleteSessionAsync(testSession.Id);

                DiagnosticInfo = $"Database Test Results:\n" +
                    $"• Settings Table: {(settings != null ? "✓" : "✗")}\n" +
                    $"• Chat Sessions: {sessions.Count()} found\n" +
                    $"• AI Models: {models.Count()} found\n" +
                    $"• Create/Read Test: {(success ? "✓" : "✗")}\n" +
                    $"• Last operation: {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                DiagnosticInfo = $"Database Error: {ex.Message}";
                _logger.LogError(ex, "Database verification failed");
            }
            finally
            {
                this.RaisePropertyChanged(nameof(DiagnosticInfo));
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
                _logger.LogInformation("Settings saved successfully");
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

        public override void Dispose()
        {
            _sensitivityDebounceTimer?.Dispose();
            base.Dispose();
        }
    }
}