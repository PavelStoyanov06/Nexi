﻿using Avalonia;
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
using Nexi.UI.Models;
using Microsoft.Extensions.Logging;

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
                configure.AddDebug(); // Logs to debug output window
                configure.AddConsole(); // Logs to console
            });

            // Register services
            services.AddSingleton<ICommandProcessor, CommandProcessor>();
            services.AddSingleton<IVoiceService, VoiceService>();
            services.AddSingleton<IChatStorageService, ChatStorageService>();
            services.AddSingleton<MainViewModel>();
            services.AddTransient<ChatHistoryViewModel>();
            services.AddTransient<ChatViewModel>();

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
            }

            base.OnFrameworkInitializationCompleted();
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