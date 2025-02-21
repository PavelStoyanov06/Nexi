using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Nexi.Services.Interfaces;
using System;

namespace Nexi.UI.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.Closed += MainWindow_Closed;
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            try
            {
                // Get voice service and ensure it's stopped
                var services = App.Current?.Services;
                if (services != null)
                {
                    var voiceService = services.GetService<IVoiceService>();
                    if (voiceService != null)
                    {
                        // Dispose voice service to ensure it's properly cleaned up
                        (voiceService as IDisposable)?.Dispose();
                    }
                }

                // Force application to exit cleanly
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during cleanup: {ex.Message}");
                // Force exit even if error
                Environment.Exit(1);
            }
        }
    }
}