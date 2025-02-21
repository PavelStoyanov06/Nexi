using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using Nexi.Services;
using Nexi.Services.Interfaces;
using Nexi.UI.Views;
using Nexi.UI.ViewModels;
using Avalonia.Themes.Fluent;
using System;
using Nexi.Data.Models;
using Microsoft.Extensions.Logging;
using Nexi.Data.Context;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Nexi.UI
{
    public partial class App : Application, IDisposable
    {
        public new static App Current => (App)Application.Current!;
        private static ThemeMode _currentTheme = ThemeMode.System;
        public IServiceProvider Services { get; }
        private bool _disposed = false;

        public App()
        {
            Services = ConfigureServices();
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Add logging
            services.AddLogging(configure =>
            {
                configure.AddDebug(); // Logs to debug output window
                configure.AddConsole(); // Logs to console
            });

            // Add DbContext factory
            services.AddDbContextFactory<NexiDbContext>(options =>
                    options.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=NexiDb;Trusted_Connection=True;MultipleActiveResultSets=true"));

            // Register services
            services.AddSingleton<ICommandProcessor, CommandProcessor>();
            services.AddSingleton<IVoiceService, VoiceService>();
            services.AddScoped<IChatStorageService, ChatStorageService>();
            services.AddScoped<IAIModelService, AIModelService>();
            services.AddScoped<IUserSettingsService, UserSettingsService>();

            // Register ViewModels
            services.AddSingleton<MainViewModel>();

            services.AddTransient(provider => {
                var storageService = provider.GetRequiredService<IChatStorageService>();
                var logger = provider.GetRequiredService<ILogger<ChatHistoryViewModel>>();
                var commandProcessor = provider.GetRequiredService<ICommandProcessor>();
                var voiceService = provider.GetRequiredService<IVoiceService>();
                var mainViewModel = provider.GetRequiredService<MainViewModel>();
                var chatViewModelLogger = provider.GetRequiredService<ILogger<ChatViewModel>>();
                return new ChatHistoryViewModel(
                    storageService, logger, commandProcessor, voiceService, mainViewModel, chatViewModelLogger);
            });

            services.AddTransient(provider => {
                var aiModelService = provider.GetRequiredService<IAIModelService>();
                var logger = provider.GetRequiredService<ILogger<ModelsViewModel>>();
                return new ModelsViewModel(aiModelService, logger);
            });

            services.AddTransient(provider => {
                var userSettingsService = provider.GetRequiredService<IUserSettingsService>();
                var aiModelService = provider.GetRequiredService<IAIModelService>();
                var voiceService = provider.GetRequiredService<IVoiceService>();
                var logger = provider.GetRequiredService<ILogger<SettingsViewModel>>();
                var chatStorageService = provider.GetRequiredService<IChatStorageService>();
                return new SettingsViewModel(userSettingsService, aiModelService, voiceService, logger, chatStorageService);
            });

            services.AddTransient(provider => {
                var commandProcessor = provider.GetRequiredService<ICommandProcessor>();
                var voiceService = provider.GetRequiredService<IVoiceService>();
                var chatStorage = provider.GetRequiredService<IChatStorageService>();
                var logger = provider.GetRequiredService<ILogger<ChatViewModel>>();
                return new ChatViewModel(commandProcessor, voiceService, chatStorage, logger);
            });

            return services.BuildServiceProvider();
        }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainViewModel = Services.GetRequiredService<MainViewModel>();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = mainViewModel
                };

                // Register for shutdown event
                desktop.ShutdownRequested += OnShutdownRequested;
                desktop.Exit += OnExit;

                // Show the window first, then load settings in background
                desktop.MainWindow.Show();

                // Load settings in the background
                Task.Run(() => SafeLoadAndApplyUserSettings());
            }

            base.OnFrameworkInitializationCompleted();
        }

        private async Task SafeLoadAndApplyUserSettings()
        {
            try
            {
                // Get the user settings service
                var userSettingsService = Services.GetRequiredService<IUserSettingsService>();
                var voiceService = Services.GetRequiredService<IVoiceService>();

                // Load settings
                var settings = await userSettingsService.GetSettingsAsync();

                // Apply settings on UI thread
                await Dispatcher.UIThread.InvokeAsync(() => {
                    // Apply theme
                    UpdateTheme(settings.SelectedTheme);

                    // Apply accent color
                    UpdateAccentColor(settings.UseSystemAccent);

                    var logger = Services.GetRequiredService<ILogger<App>>();
                    logger.LogInformation("Settings applied: Theme={Theme}", settings.SelectedTheme);
                });

                // Apply voice settings (can be done off UI thread)
                await voiceService.UpdateInputDeviceAsync(
                    settings.SelectedInputDevice ?? "Default",
                    settings.InputSensitivity);

                var logger = Services.GetRequiredService<ILogger<App>>();
                logger.LogInformation("Voice settings applied: InputDevice={Device}, Sensitivity={Sensitivity}",
                    settings.SelectedInputDevice, settings.InputSensitivity);
            }
            catch (Exception ex)
            {
                // Get logger and log the error
                var logger = Services.GetRequiredService<ILogger<App>>();
                logger.LogError(ex, "Error loading and applying user settings at startup");
            }
        }

        // Remove the LoadAndApplyUserSettingsSync method

        private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
        {
            // Perform cleanup before shutdown
            CleanupResources();
        }

        private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
        {
            // Final cleanup on exit
            CleanupResources();
        }

        private void CleanupResources()
        {
            try
            {
                // Stop voice service
                var voiceService = Services.GetService<IVoiceService>();
                if (voiceService != null && voiceService.IsListening)
                {
                    voiceService.StopListeningAsync().GetAwaiter().GetResult();
                }

                // Dispose the service provider if it's disposable
                if (Services is IDisposable disposableServices)
                {
                    disposableServices.Dispose();
                }
            }
            catch (Exception ex)
            {
                var logger = Services.GetService<ILogger<App>>();
                logger?.LogError(ex, "Error during application cleanup");
            }
        }

        private void LoadAndApplyUserSettingsSync()
        {
            try
            {
                // Get the user settings service
                var userSettingsService = Services.GetRequiredService<IUserSettingsService>();
                var voiceService = Services.GetRequiredService<IVoiceService>();

                // Load settings synchronously
                var settings = userSettingsService.GetSettingsAsync().GetAwaiter().GetResult();

                // Apply theme
                UpdateTheme(settings.SelectedTheme);

                // Apply accent color
                UpdateAccentColor(settings.UseSystemAccent);

                // Apply voice settings
                voiceService.UpdateInputDeviceAsync(
                    settings.SelectedInputDevice ?? "Default",
                    settings.InputSensitivity
                ).GetAwaiter().GetResult();

                var logger = Services.GetRequiredService<ILogger<App>>();
                logger.LogInformation("Settings applied at startup: Theme={Theme}, InputDevice={Device}, Sensitivity={Sensitivity}",
                    settings.SelectedTheme, settings.SelectedInputDevice, settings.InputSensitivity);
            }
            catch (Exception ex)
            {
                // Get logger and log the error
                var logger = Services.GetRequiredService<ILogger<App>>();
                logger.LogError(ex, "Error loading and applying user settings at startup");
            }
        }

        public static ThemeMode CurrentTheme
        {
            get => _currentTheme;
            private set => _currentTheme = value;
        }

        public static void UpdateTheme(ThemeMode mode)
        {
            if (Current != null)
            {
                CurrentTheme = mode; // Store the selected theme
                switch (mode)
                {
                    case ThemeMode.Light:
                        Current.RequestedThemeVariant = ThemeVariant.Light;
                        break;
                    case ThemeMode.Dark:
                        Current.RequestedThemeVariant = ThemeVariant.Dark;
                        break;
                    case ThemeMode.System:
                        Current.RequestedThemeVariant = null;
                        break;
                }
            }
        }

        public static void UpdateAccentColor(bool useSystem)
        {
            if (Current?.Styles[0] is FluentTheme fluentTheme)
            {
                // Placeholder for accent color logic
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    CleanupResources();
                }

                _disposed = true;
            }
        }
    }
}