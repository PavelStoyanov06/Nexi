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
using Microsoft.EntityFrameworkCore;
using Nexi.Data.Context;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Nexi.UI
{
    public partial class App : Application
    {
        public new static App Current => (App)Application.Current!;
        private static ThemeMode _currentTheme = ThemeMode.System;
        public IServiceProvider Services { get; }

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

            // Add DbContext Factory instead of DbContext
            services.AddDbContextFactory<NexiDbContext>(options =>
                options.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=NexiDb;Trusted_Connection=True;MultipleActiveResultSets=true"));

            // Register services
            services.AddSingleton<ICommandProcessor, CommandProcessor>();
            services.AddSingleton<IVoiceService, VoiceService>();
            services.AddScoped<IChatStorageService, ChatStorageService>();
            services.AddScoped<IAIModelService, AIModelService>();
            services.AddScoped<IUserSettingsService, UserSettingsService>();
            
            // Register LlamaSharp service as a singleton with proper disposal
            services.AddSingleton<ILlamaSharpService>(provider => {
                var logger = provider.GetRequiredService<ILogger<LlamaSharpService>>();
                return new LlamaSharpService(logger);
            });
            
            services.AddHttpClient();

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
                var userSettingsService = provider.GetRequiredService<IUserSettingsService>();
                var logger = provider.GetRequiredService<ILogger<ModelsViewModel>>();
                return new ModelsViewModel(aiModelService, userSettingsService, logger);
            });

            services.AddTransient(provider => {
                var userSettingsService = provider.GetRequiredService<IUserSettingsService>();
                var aiModelService = provider.GetRequiredService<IAIModelService>();
                var logger = provider.GetRequiredService<ILogger<SettingsViewModel>>();
                return new SettingsViewModel(userSettingsService, aiModelService, logger);
            });

            services.AddTransient(provider => {
                var commandProcessor = provider.GetRequiredService<ICommandProcessor>();
                var voiceService = provider.GetRequiredService<IVoiceService>();
                var chatStorage = provider.GetRequiredService<IChatStorageService>();
                var aiModelService = provider.GetRequiredService<IAIModelService>();
                var userSettingsService = provider.GetRequiredService<IUserSettingsService>();
                var llamaSharpService = provider.GetRequiredService<ILlamaSharpService>();
                var logger = provider.GetRequiredService<ILogger<ChatViewModel>>();
                return new ChatViewModel(commandProcessor, voiceService, chatStorage, aiModelService, userSettingsService, llamaSharpService, logger);
            });

            return services.BuildServiceProvider();
        }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // Set up global exception handling
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var logger = Services.GetService(typeof(ILogger<App>)) as ILogger<App>;
                var exception = args.ExceptionObject as Exception;
                
                if (exception is AccessViolationException)
                {
                    logger?.LogError(exception, "Unhandled AccessViolationException. This is likely due to a memory issue with the LlamaSharp native library.");
                    
                    // Try to clean up resources
                    try
                    {
                        var llamaService = Services.GetService(typeof(ILlamaSharpService)) as ILlamaSharpService;
                        if (llamaService != null && llamaService is IDisposable disposable)
                        {
                            logger?.LogInformation("Attempting to dispose LlamaSharpService to recover from AccessViolationException");
                            disposable.Dispose();
                        }
                        
                        // Force garbage collection
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                    }
                    catch (Exception cleanupEx)
                    {
                        logger?.LogError(cleanupEx, "Error during cleanup after AccessViolationException");
                    }
                }
                else
                {
                    logger?.LogError(exception, "Unhandled exception: {Message}", exception?.Message);
                }
            };
            
            // Also handle UI thread exceptions
            Dispatcher.UIThread.UnhandledException += (sender, e) =>
            {
                var logger = Services.GetService(typeof(ILogger<App>)) as ILogger<App>;
                
                if (e.Exception is AccessViolationException)
                {
                    logger?.LogError(e.Exception, "Unhandled AccessViolationException in UI thread");
                    
                    // Try to clean up resources
                    try
                    {
                        var llamaService = Services.GetService(typeof(ILlamaSharpService)) as ILlamaSharpService;
                        if (llamaService != null && llamaService is IDisposable disposable)
                        {
                            logger?.LogInformation("Attempting to dispose LlamaSharpService to recover from AccessViolationException");
                            disposable.Dispose();
                        }
                        
                        // Force garbage collection
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                        
                        // Mark as handled to prevent app crash
                        e.Handled = true;
                    }
                    catch (Exception cleanupEx)
                    {
                        logger?.LogError(cleanupEx, "Error during cleanup after AccessViolationException");
                    }
                }
                else
                {
                    logger?.LogError(e.Exception, "Unhandled exception in UI thread: {Message}", e.Exception?.Message);
                    e.Handled = true;
                }
            };

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Create the main window
                var mainWindow = new MainWindow
                {
                    DataContext = Services.GetRequiredService<MainViewModel>(),
                };

                desktop.MainWindow = mainWindow;
                
                // Register application exit handler
                desktop.Exit += OnApplicationExit;

                // Ensure database is created
                using var scope = Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<NexiDbContext>();
                dbContext.Database.EnsureCreated();

                // Load user settings on startup
                _ = LoadAndApplyUserSettingsAsync();
            }

            base.OnFrameworkInitializationCompleted();
        }

        private async Task LoadAndApplyUserSettingsAsync()
        {
            try
            {
                // Get the user settings service
                var userSettingsService = Services.GetRequiredService<IUserSettingsService>();

                // Load settings
                var settings = await userSettingsService.GetSettingsAsync();

                // Apply theme
                UpdateTheme(settings.SelectedTheme);

                // Apply accent color
                UpdateAccentColor(settings.UseSystemAccent);

                // Here you could apply other global settings as needed
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

        private void OnApplicationExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
        {
            var logger = Services.GetService(typeof(ILogger<App>)) as ILogger<App>;
            logger?.LogInformation("Application is shutting down, cleaning up resources");
            
            try
            {
                // Dispose LlamaSharpService
                var llamaService = Services.GetService(typeof(ILlamaSharpService)) as ILlamaSharpService;
                if (llamaService != null && llamaService is IDisposable disposableLlama)
                {
                    logger?.LogInformation("Disposing LlamaSharpService on application exit");
                    disposableLlama.Dispose();
                }
                
                // Dispose VoiceService
                var voiceService = Services.GetService(typeof(IVoiceService)) as IVoiceService;
                if (voiceService != null && voiceService is IDisposable disposableVoice)
                {
                    logger?.LogInformation("Disposing VoiceService on application exit");
                    disposableVoice.Dispose();
                }
                
                // Dispose MainViewModel
                var mainViewModel = Services.GetService(typeof(MainViewModel)) as MainViewModel;
                if (mainViewModel != null)
                {
                    logger?.LogInformation("Disposing MainViewModel on application exit");
                    mainViewModel.Dispose();
                }
                
                // Force garbage collection
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error during application exit cleanup");
            }
        }
    }
}