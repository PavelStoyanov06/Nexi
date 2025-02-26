using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexi.Data.Models;
using Nexi.Services.AI;
using Nexi.UI.Services;
using Nexi.UI.ViewModels;
using Nexi.UI.Views;
using System;

namespace Nexi.UI
{
    public class App : Application
    {
        private static IServiceProvider? _services;
        private static Window _mainWindow;
        private AuthenticationHandler _authenticationHandler;

        // Single property for Services that can be both accessed statically and set from outside
        public static IServiceProvider? Services
        {
            get => _services;
            set => _services = value;
        }

        public static Window MainWindow { get; private set; }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Check if services were provided from Program.cs
                if (_services == null)
                {
                    // If not, create services here (fallback)
                    var serviceCollection = new ServiceCollection();
                    ConfigureServices(serviceCollection);
                    _services = serviceCollection.BuildServiceProvider();
                }

                // Set up main window and viewmodel
                var mainViewModel = _services.GetRequiredService<MainViewModel>();
                MainWindow = new MainWindow
                {
                    DataContext = mainViewModel
                };
                desktop.MainWindow = MainWindow;

                // Initialize the authentication handler after the main window is created
                // This connects the UI layer to the service layer
                var logger = _services.GetRequiredService<ILogger<AuthenticationHandler>>();
                var authHandler = new AuthenticationHandler(logger, MainWindow);

                // Store the handler so it doesn't get garbage collected
                _authenticationHandler = authHandler;
            }

            base.OnFrameworkInitializationCompleted();
        }



        private void ConfigureServices(IServiceCollection services)
        {
            // Register services
            services.AddLogging(configure => configure.AddConsole().AddDebug());

            // Register token service
            services.AddSingleton<TokenService>();

            // Register view models as transient
            services.AddTransient<MainViewModel>();
            services.AddTransient<ChatHistoryViewModel>();
            services.AddTransient<ModelsViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<ApiTokensViewModel>();
        }


        // Static methods for theme and accent color management
        public static void UpdateTheme(ThemeMode mode)
        {
            var app = Current as App;
            if (app == null) return;

            var requestedThemeVariant = mode switch
            {
                ThemeMode.Light => ThemeVariant.Light,
                ThemeMode.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };

            app.RequestedThemeVariant = requestedThemeVariant;
        }

        public static void UpdateAccentColor(bool useSystem)
        {
            // For now, this is a placeholder implementation
            var app = Current as App;
            if (app == null) return;

            // Here you would update the application resources with the new accent color
            // app.Resources["SystemAccentColor"] = new SolidColorBrush(color);
        }
    }
}