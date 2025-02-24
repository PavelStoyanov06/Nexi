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
        private readonly CancellationTokenSource _cleanupCts = new CancellationTokenSource();

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

                // Run cleanup in the background without awaiting
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
                var app = App.Current;
                if (app == null)
                {
                    return;
                }

                var services = app.Services;
                if (services == null)
                {
                    return;
                }

                // Stop voice service first as it's most likely to hang
                var voiceService = services.GetService<IVoiceService>();
                if (voiceService != null)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    try
                    {
                        await voiceService.StopListeningAsync().WaitAsync(cts.Token);

                        // Only dispose if it implements IDisposable
                        (voiceService as IDisposable)?.Dispose();
                    }
                    catch (OperationCanceledException)
                    {
                        // Timeout - force disposal if possible
                        (voiceService as IDisposable)?.Dispose();
                    }
                    catch (Exception)
                    {
                        // Ignore any exceptions during cleanup
                    }
                }

                // We'll avoid explicit disposal of the service provider here
                // since it can lead to ObjectDisposedException when services
                // are still being used during shutdown

                // However, we can still cancel the cleanup token to signal operations to stop
                _cleanupCts.Cancel();

                // Force exit immediately after cleanup
                try
                {
                    Environment.Exit(0);
                }
                catch
                {
                    // Ignore - we're shutting down anyway
                }
            }
            catch (Exception)
            {
                // If any cleanup fails, force exit
                try
                {
                    Environment.Exit(1);
                }
                catch
                {
                    // Ignore - we're shutting down anyway
                }
            }
            finally
            {
                _cleanupCts.Dispose();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Make sure we dispose the cleanup token
            _cleanupCts.Dispose();
        }
    }
}