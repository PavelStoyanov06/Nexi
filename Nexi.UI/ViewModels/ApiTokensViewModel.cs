using Interfaces;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Nexi.UI.ViewModels
{
    public class ApiTokensViewModel : ViewModelBase
    {
        private readonly IAuthenticationService _authService;
        private readonly ILogger<ApiTokensViewModel> _logger;

        private string _huggingFaceToken = string.Empty;
        private bool _showHuggingFaceToken;
        private string _statusMessage = string.Empty;
        private bool _statusIsError;

        public ApiTokensViewModel(IAuthenticationService authService, ILogger<ApiTokensViewModel> logger)
        {
            _authService = authService;
            _logger = logger;

            // Initialize commands
            SaveHuggingFaceTokenCommand = ReactiveCommand.CreateFromTask(SaveHuggingFaceTokenAsync);
            ClearHuggingFaceTokenCommand = ReactiveCommand.CreateFromTask(ClearHuggingFaceTokenAsync);
            OpenHuggingFaceTokenPageCommand = ReactiveCommand.Create(OpenHuggingFaceTokenPage);

            // Load existing tokens
            _ = LoadTokensAsync();
        }

        public string HuggingFaceToken
        {
            get => _huggingFaceToken;
            set => this.RaiseAndSetIfChanged(ref _huggingFaceToken, value);
        }

        public bool ShowHuggingFaceToken
        {
            get => _showHuggingFaceToken;
            set => this.RaiseAndSetIfChanged(ref _showHuggingFaceToken, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public bool StatusIsError
        {
            get => _statusIsError;
            set => this.RaiseAndSetIfChanged(ref _statusIsError, value);
        }

        public ICommand SaveHuggingFaceTokenCommand { get; }
        public ICommand ClearHuggingFaceTokenCommand { get; }
        public ICommand OpenHuggingFaceTokenPageCommand { get; }

        private async Task LoadTokensAsync()
        {
            try
            {
                var token = await _authService.GetTokenAsync("HuggingFace");
                if (!string.IsNullOrEmpty(token))
                {
                    HuggingFaceToken = token;
                    StatusMessage = "Token loaded successfully";
                    StatusIsError = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading tokens");
                StatusMessage = "Error loading token: " + ex.Message;
                StatusIsError = true;
            }
        }

        private async Task SaveHuggingFaceTokenAsync()
        {
            if (string.IsNullOrWhiteSpace(HuggingFaceToken))
            {
                StatusMessage = "Please enter a valid token";
                StatusIsError = true;
                return;
            }

            try
            {
                await _authService.SaveTokenAsync("HuggingFace", HuggingFaceToken);
                StatusMessage = "Token saved successfully";
                StatusIsError = false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving HuggingFace token");
                StatusMessage = "Error saving token: " + ex.Message;
                StatusIsError = true;
            }
        }

        private async Task ClearHuggingFaceTokenAsync()
        {
            try
            {
                await _authService.ClearTokenAsync("HuggingFace");
                HuggingFaceToken = string.Empty;
                StatusMessage = "Token cleared successfully";
                StatusIsError = false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing HuggingFace token");
                StatusMessage = "Error clearing token: " + ex.Message;
                StatusIsError = true;
            }
        }

        private void OpenHuggingFaceTokenPage()
        {
            try
            {
                string url = "https://huggingface.co/settings/tokens";

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }

                StatusMessage = "Opening HuggingFace token page in your browser";
                StatusIsError = false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening HuggingFace token page");
                StatusMessage = "Could not open browser: " + ex.Message;
                StatusIsError = true;
            }
        }
    }
}