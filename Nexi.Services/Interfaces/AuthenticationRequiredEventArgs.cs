using System;
using System.Collections.Generic;
using System.Text;

namespace Interfaces
{
    public class AuthenticationRequiredEventArgs : EventArgs
    {
        /// <summary>
        /// The provider that requires authentication (e.g., "HuggingFace")
        /// </summary>
        public string Provider { get; }

        /// <summary>
        /// A description of what requires authentication (e.g., model name)
        /// </summary>
        public string ResourceName { get; }

        /// <summary>
        /// Set this to the token to use for authentication
        /// </summary>
        public string Token { get; set; }

        /// <summary>
        /// Set to true if the token should be saved for future use
        /// </summary>
        public bool SaveToken { get; set; }

        /// <summary>
        /// Set to true if authentication was successful, false if it was canceled
        /// </summary>
        public bool IsAuthenticated { get; set; }

        public AuthenticationRequiredEventArgs(string provider, string resourceName)
        {
            Provider = provider;
            ResourceName = resourceName;
            Token = string.Empty;
            SaveToken = true;
            IsAuthenticated = false;
        }
    }

}
