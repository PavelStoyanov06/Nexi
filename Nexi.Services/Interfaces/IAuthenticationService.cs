using System;
using System.Collections.Generic;
using System.Text;

namespace Interfaces
{
    public interface IAuthenticationService
    {
        /// <summary>
        /// Checks if a token exists for the specified provider
        /// </summary>
        Task<bool> HasTokenAsync(string provider);

        /// <summary>
        /// Gets a token for the specified provider
        /// </summary>
        Task<string> GetTokenAsync(string provider);

        /// <summary>
        /// Saves a token for the specified provider
        /// </summary>
        Task SaveTokenAsync(string provider, string token);

        /// <summary>
        /// Clears a token for the specified provider
        /// </summary>
        Task ClearTokenAsync(string provider);

        /// <summary>
        /// Event that's raised when authentication is required but no token is available
        /// </summary>
        event EventHandler<AuthenticationRequiredEventArgs> AuthenticationRequired;
    }
}
