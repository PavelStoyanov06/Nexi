using Interfaces;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;

namespace Nexi.Services.AI
{
    public static class ModelRepositoryExtensions
    {
        /// <summary>
        /// Downloads a model that requires authentication using the appropriate token
        /// </summary>
        public static async Task<byte[]> DownloadModelWithAuthAsync(
            this ModelRepository repository,
            string modelUrl,
            string provider,
            string modelName,
            IAuthenticationService authService,
            ILogger logger)
        {
            try
            {
                // Get the token - this will trigger the auth dialog if needed
                var token = await authService.RequestAuthenticationAsync(provider, modelName);

                // If we get here, we have a token
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Nexi", "1.0"));
                httpClient.Timeout = TimeSpan.FromMinutes(30); // Long timeout for large downloads

                var response = await httpClient.GetAsync(modelUrl);

                // Handle specific authorization errors
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    // Clear the token as it might be invalid
                    await authService.ClearTokenAsync(provider);

                    // Throw a more specific exception
                    throw new UnauthorizedAccessException(
                        "The provided token was rejected. Please check your credentials and try again.");
                }

                // Ensure we got a success response
                response.EnsureSuccessStatusCode();

                // Return the file bytes
                return await response.Content.ReadAsByteArrayAsync();
            }
            catch (HttpRequestException ex)
            {
                logger.LogError(ex, "Error downloading model with authentication");

                if (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    // Clear the token as it might be invalid
                    await authService.ClearTokenAsync(provider);

                    // Throw a more specific exception
                    throw new UnauthorizedAccessException(
                        "The provided token was rejected. Please check your credentials and try again.",
                        ex);
                }

                throw;
            }
        }
    }
}