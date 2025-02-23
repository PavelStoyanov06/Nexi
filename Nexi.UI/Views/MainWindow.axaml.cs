using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Nexi.Services.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nexi.UI.Views
{
    public partial class MainWindow : Window
    {
        private bool _isClosing = false;

        public MainWindow()
        {
            InitializeComponent();
            this.Closing += MainWindow_Closing;
            this.Closed += MainWindow_Closed;
        }

        private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            if (!_isClosing)
            {
                _isClosing = true;
                Task.Run(CleanupAsync).ConfigureAwait(false);
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            // Force immediate exit if cleanup is taking too long
            Environment.Exit(0);
        }

        private async Task CleanupAsync()
        {
            try
            {
                var services = App.Current?.Services;
                if (services != null)
                {
                    // Stop voice service first as it's most likely to hang
                    var voiceService = services.GetService<IVoiceService>();
                    if (voiceService != null)
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        try
                        {
                            await voiceService.StopListeningAsync().WaitAsync(cts.Token);
                            (voiceService as IDisposable)?.Dispose();
                        }
                        catch (OperationCanceledException)
                        {
                            // Timeout - force disposal
                            (voiceService as IDisposable)?.Dispose();
                        }
                    }

                    // Dispose the service provider
                    if (services is IDisposable disposableServices)
                    {
                        disposableServices.Dispose();
                    }

                    // Force exit immediately after cleanup
                    Environment.Exit(0);
                }
            }
            catch (Exception)
            {
                // If any cleanup fails, force exit
                Environment.Exit(1);
            }
        }
    }
}