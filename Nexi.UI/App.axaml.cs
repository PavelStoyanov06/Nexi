using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Nexi.UI.Views;
using Nexi.UI.ViewModels;
using Avalonia.Themes.Fluent;

namespace Nexi.UI
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }

        public static void UpdateTheme(ThemeMode mode)
        {
            if (Current != null)
            {
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