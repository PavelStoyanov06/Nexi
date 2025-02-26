using Avalonia.Controls;
using Avalonia.Threading;
using Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexi.UI.ViewModels;
using Nexi.UI.Views;
using System;
using System.Threading.Tasks;

namespace Nexi.UI.Services
{
    /// <summary>
    /// Handles authentication requests from the service layer by showing UI dialogs
    /// </summary>
    public class AuthenticationHandler
    {
        private readonly ILogger<AuthenticationHandler> _logger;
        private readonly Window _mainWindow;

        public AuthenticationHandler(ILogger<AuthenticationHandler> logger, Window mainWindow)
        {
            _logger = logger;
            _mainWindow = mainWindow;

            // Get the authentication service
            var authService = App.Services.GetRequiredService<IAuthenticationService>();

            // Subscribe to authentication requests
            authService.AuthenticationRequired += OnAuthenticationRequired;
        }

        private void OnAuthenticationRequired(object sender, AuthenticationRequiredEventArgs e)
        {
            // We need to use a task completion source to make this async operation work synchronously
            // as the event handler is synchronous but we need to wait for dialog result
            var tcs = new TaskCompletionSource<bool>();

            // UI operations must be done on the UI thread
            Dispatcher.UIThread.Post(async () => {
                try
                {
                    // Create the token required view model
                    var viewModel = new TokenRequiredViewModel(
                        App.Services.GetRequiredService<IAuthenticationService>(),
                        App.Services.GetRequiredService<ILogger<TokenRequiredViewModel>>(),
                        e.ResourceName,
                        e.Provider);

                    // Set up the dialog
                    var dialog = new TokenRequiredDialog
                    {
                        DataContext = viewModel
                    };

                    // Show the dialog
                    bool? result = await dialog.ShowDialog<bool?>(_mainWindow);

                    if (result == true && !string.IsNullOrEmpty(viewModel.Token))
                    {
                        // Dialog was accepted and token was provided
                        e.Token = viewModel.Token;
                        e.SaveToken = viewModel.SaveToken;
                        e.IsAuthenticated = true;
                        tcs.SetResult(true);
                    }
                    else
                    {
                        // Dialog was canceled
                        e.IsAuthenticated = false;
                        tcs.SetResult(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error showing authentication dialog");
                    e.IsAuthenticated = false;
                    tcs.SetException(ex);
                }
            });

            // Wait for the dialog to complete
            // Note: this will block the current thread until the dialog is closed
            // This is required because the event handler is synchronous
            // In a real-world application, you might want to use a different approach
            tcs.Task.Wait();
        }

        public void Dispose()
        {
            // Unsubscribe from authentication events
            var authService = App.Services.GetRequiredService<IAuthenticationService>();
            authService.AuthenticationRequired -= OnAuthenticationRequired;
        }
    }
}