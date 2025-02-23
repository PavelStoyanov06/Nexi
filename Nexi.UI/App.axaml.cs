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
using System.Linq;
using Avalonia.Threading;
using System.Threading;

namespace Nexi.UI
{
    public partial class App : Application, IDisposable
    {
        public new static App Current => (App)Application.Current!;
        private static ThemeMode _currentTheme = ThemeMode.System;
        public IServiceProvider Services { get; }
        private bool _disposed;
        private readonly CancellationTokenSource _cleanupCts = new();

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
                configure.AddDebug();
                configure.AddConsole();
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

        private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
        {
            CleanupResources();
        }

        private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
        {
            try
            {
                // Cancel any ongoing operations
                _cleanupCts.Cancel();

                // Force cleanup with timeout
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                Task.WhenAny(CleanupAsync(), Task.Delay(3000, timeoutCts.Token))
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                // Ensure we exit even if cleanup fails
                Environment.Exit(1);
            }
        }

        private async Task CleanupAsync()
        {
            try
            {
                // Get all disposable services
                var disposableServices = Services.GetServices<IDisposable>();

                // Create cleanup tasks for all services
                var cleanupTasks = disposableServices.Select(service => Task.Run(() =>
                {
                    try
                    {
                        service.Dispose();
                    }
                    catch
                    {
                        // Ignore individual service cleanup failures
                    }
                })).ToList();

                // Wait for all cleanup tasks with timeout
                await Task.WhenAll(cleanupTasks).WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        private void CleanupResources()
        {
            try
            {
                // Stop voice service
                var voiceService = Services.GetService<IVoiceService>();
                if (voiceService != null && voiceService.IsListening)
                {
                    voiceService.StopListeningAsync().Wait(TimeSpan.FromSeconds(2));
                }

                // Dispose the service provider if it's disposable
                if (Services is IDisposable disposableServices)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    Task.Run(() => disposableServices.Dispose(), cts.Token).Wait(cts.Token);
                }
            }
            catch (Exception ex)
            {
                var logger = Services.GetService<ILogger<App>>();
                logger?.LogError(ex, "Error during application cleanup");
            }
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

        public static ThemeMode CurrentTheme
        {
            get => _currentTheme;
            private set => _currentTheme = value;
        }

        public static void UpdateTheme(ThemeMode mode)
        {
            if (Current != null)
            {
                CurrentTheme = mode;
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
                    _cleanupCts.Cancel();
                    _cleanupCts.Dispose();
                    CleanupResources();
                }

                _disposed = true;
            }
        }
    }
}