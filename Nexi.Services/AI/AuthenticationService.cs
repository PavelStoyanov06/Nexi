using Interfaces;
using Microsoft.Extensions.Logging;
using Nexi.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Nexi.Services.AI
{
    public class AuthenticationService : IAuthenticationService
    {
        private readonly ILogger<AuthenticationService> _logger;
        private readonly string _encryptedConfigPath;
        private readonly byte[] _entropy; // Randomization factor for encryption

        public event EventHandler<AuthenticationRequiredEventArgs> AuthenticationRequired;

        public AuthenticationService(ILogger<AuthenticationService> logger)
        {
            _logger = logger;
            _encryptedConfigPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Nexi", "secure", "tokens.dat");

            // Generate or retrieve entropy value
            _entropy = GetOrCreateEntropy();
        }

        public async Task<bool> HasTokenAsync(string provider)
        {
            var token = await GetTokenAsync(provider);
            return !string.IsNullOrEmpty(token);
        }

        public async Task SaveTokenAsync(string provider, string token)
        {
            try
            {
                var tokens = await LoadTokensAsync();
                tokens[provider] = token;

                // Encrypt and save tokens
                var json = JsonSerializer.Serialize(tokens);
                byte[] dataToEncrypt = Encoding.UTF8.GetBytes(json);
                byte[] encryptedData = ProtectedData.Protect(dataToEncrypt, _entropy, DataProtectionScope.CurrentUser);

                Directory.CreateDirectory(Path.GetDirectoryName(_encryptedConfigPath));
                await File.WriteAllBytesAsync(_encryptedConfigPath, encryptedData);

                _logger.LogInformation("Token saved for provider: {Provider}", provider);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving token for provider: {Provider}", provider);
                throw;
            }
        }

        public async Task<string> GetTokenAsync(string provider)
        {
            try
            {
                var tokens = await LoadTokensAsync();
                return tokens.TryGetValue(provider, out var token) ? token : string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving token for provider: {Provider}", provider);
                return string.Empty;
            }
        }

        public async Task ClearTokenAsync(string provider)
        {
            try
            {
                var tokens = await LoadTokensAsync();
                if (tokens.ContainsKey(provider))
                {
                    tokens.Remove(provider);

                    var json = JsonSerializer.Serialize(tokens);
                    byte[] dataToEncrypt = Encoding.UTF8.GetBytes(json);
                    byte[] encryptedData = ProtectedData.Protect(dataToEncrypt, _entropy, DataProtectionScope.CurrentUser);

                    Directory.CreateDirectory(Path.GetDirectoryName(_encryptedConfigPath));
                    await File.WriteAllBytesAsync(_encryptedConfigPath, encryptedData);

                    _logger.LogInformation("Token cleared for provider: {Provider}", provider);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing token for provider: {Provider}", provider);
                throw;
            }
        }

        public async Task<string> RequestAuthenticationAsync(string provider, string resourceName)
        {
            // First check if we already have a token
            string token = await GetTokenAsync(provider);
            if (!string.IsNullOrEmpty(token))
            {
                return token;
            }

            // If no token is available, raise the event to request authentication
            var args = new AuthenticationRequiredEventArgs(provider, resourceName);

            AuthenticationRequired?.Invoke(this, args);

            // Check if authentication was provided
            if (args.IsAuthenticated && !string.IsNullOrEmpty(args.Token))
            {
                // Save the token if requested
                if (args.SaveToken)
                {
                    await SaveTokenAsync(provider, args.Token);
                }

                return args.Token;
            }

            // If we get here, authentication was canceled or failed
            throw new OperationCanceledException($"Authentication required for {resourceName} but was not provided");
        }

        private async Task<Dictionary<string, string>> LoadTokensAsync()
        {
            try
            {
                if (!File.Exists(_encryptedConfigPath))
                    return new Dictionary<string, string>();

                byte[] encryptedData = await File.ReadAllBytesAsync(_encryptedConfigPath);
                byte[] decryptedData = ProtectedData.Unprotect(encryptedData, _entropy, DataProtectionScope.CurrentUser);
                string json = Encoding.UTF8.GetString(decryptedData);

                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading tokens");
                return new Dictionary<string, string>();
            }
        }

        private byte[] GetOrCreateEntropy()
        {
            try
            {
                string entropyPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Nexi", "secure", "entropy.dat");

                if (File.Exists(entropyPath))
                {
                    return File.ReadAllBytes(entropyPath);
                }

                // Create new entropy
                byte[] entropy = new byte[16];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(entropy);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(entropyPath));
                File.WriteAllBytes(entropyPath, entropy);

                _logger.LogInformation("Created new entropy for token encryption");
                return entropy;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating/retrieving entropy. Using fallback entropy value");

                // If we can't create or retrieve entropy, use a fallback
                // Still better than no encryption, though less secure
                return Encoding.UTF8.GetBytes("NexiAppDefaultEntropy");
            }
        }
    }
}