using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Interfaces;
using Microsoft.Extensions.Logging;
using Nexi.Services.Interfaces;
using ReactiveUI;

namespace Nexi.UI.ViewModels
{
    public class TokenRequiredViewModel : ViewModelBase
    {
        private readonly IAuthenticationService _authService;
        private readonly ILogger<TokenRequiredViewModel> _logger;

        private string _token = string.Empty;
        private bool _showToken;
        private bool _saveToken = true;
        private string _explanationText;
        private string _statusMessage = string.Empty;
        private bool _statusIsError;
        private bool _dialogResult;
        private string _provider;

        public event EventHandler<bool> RequestClose;

        public TokenRequiredViewModel(
            IAuthenticationService authService,
            ILogger<TokenRequiredViewModel> logger,
            string modelName,
            string provider = "HuggingFace")
        {
            _authService = authService;
            _logger = logger;
            _provider = provider;

            // Set explanation text
            _explanationText = $"The model \"{modelName}\" requires authentication to download. " +
                              $"Please enter your {provider} access token to continue.";

            // Initialize commands
            ContinueCommand = ReactiveCommand.CreateFromTask(ContinueAsync);
            CancelCommand = ReactiveCommand.Create(Cancel);
            OpenHuggingFaceTokenPageCommand = ReactiveCommand.Create(OpenHuggingFaceTokenPage);

            // Load existing token if available
            _ = LoadTokenAsync();
        }

        public string Token
        {
            get => _token;
            set
            {
                this.RaiseAndSetIfChanged(ref _token, value);
                this.RaisePropertyChanged(nameof(CanContinue));
            }
        }

        public bool ShowToken
        {
            get => _showToken;
            set => this.RaiseAndSetIfChanged(ref _showToken, value);
        }

        public bool SaveToken
        {
            get => _saveToken;
            set => this.RaiseAndSetIfChanged(ref _saveToken, value);
        }

        public string ExplanationText
        {
            get => _explanationText;
            set => this.RaiseAndSetIfChanged(ref _explanationText, value);
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

        public bool CanContinue => !string.IsNullOrWhiteSpace(Token);

        public bool DialogResult => _dialogResult;

        public ICommand ContinueCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand OpenHuggingFaceTokenPageCommand { get; }

        private async Task LoadTokenAsync()
        {
            try
            {
                var token = await _authService.GetTokenAsync(_provider);
                if (!string.IsNullOrEmpty(token))
                {
                    Token = token;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading token");
                StatusMessage = "Could not load existing token";
                StatusIsError = true;
            }
        }

        private async Task ContinueAsync()
        {
            if (string.IsNullOrWhiteSpace(Token))
            {
                StatusMessage = "Please enter a valid token";
                StatusIsError = true;
                return;
            }

            try
            {
                // We'll save the token later through the authentication service
                // through the event args if the user requested it

                // Set success and close
                _dialogResult = true;
                RequestClose?.Invoke(this, true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in continue action");
                StatusMessage = "Error: " + ex.Message;
                StatusIsError = true;
            }
        }

        private void Cancel()
        {
            _dialogResult = false;
            RequestClose?.Invoke(this, false);
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening token page");
                StatusMessage = "Could not open browser: " + ex.Message;
                StatusIsError = true;
            }
        }
    }
}