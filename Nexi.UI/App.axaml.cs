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
            
            // Register new services for LlamaSharp
            services.AddSingleton<ILlamaSharpService, LlamaSharpService>();
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

        public override async void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Set up global exception handling
                AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                {
                    var logger = Services.GetRequiredService<ILogger<App>>();
                    logger.LogError(e.ExceptionObject as Exception, "Unhandled exception in application");
                    
                    // We can't prevent the app from terminating in this event handler,
                    // but we can log the error and perform cleanup
                    try
                    {
                        // Perform any necessary cleanup
                        logger.LogInformation("Performing cleanup before application termination");
                    }
                    catch
                    {
                        // Suppress any exceptions in the exception handler
                    }
                };
                
                // Also handle exceptions in the UI thread
                Dispatcher.UIThread.UnhandledException += (sender, e) =>
                {
                    var logger = Services.GetRequiredService<ILogger<App>>();
                    logger.LogError(e.Exception, "Unhandled exception in UI thread");
                    
                    // Mark as handled to prevent app crash
                    e.Handled = true;
                };
                
                var mainViewModel = Services.GetRequiredService<MainViewModel>();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = mainViewModel
                };

                // Ensure database is created
                using (var scope = Services.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<NexiDbContext>();
                    await dbContext.Database.EnsureCreatedAsync();
                }

                // Load and apply user settings on startup
                await LoadAndApplyUserSettingsAsync();
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
    }
}