using Nexi.Data.Models;

namespace Nexi.Services.Interfaces
{
    public interface IAIService
    {
        /// <summary>
        /// Event raised during inference to report progress
        /// </summary>
        event EventHandler<string>? OnInferenceProgress;

        /// <summary>
        /// Event raised when an error occurs
        /// </summary>
        event EventHandler<Exception>? OnError;

        /// <summary>
        /// Check if a model is currently loaded in memory
        /// </summary>
        bool IsModelLoaded(string modelId);

        /// <summary>
        /// Get a text completion from the AI model
        /// </summary>
        Task<AIResponse> GetCompletionAsync(string prompt, AIRequestOptions options);

        /// <summary>
        /// Get a text completion with conversation history
        /// </summary>
        Task<AIResponse> GetCompletionWithHistoryAsync(
            IEnumerable<(bool IsUser, string Message)> history,
            string prompt,
            AIRequestOptions options);

        /// <summary>
        /// Download a model from the repository
        /// </summary>
        Task DownloadModelAsync(string modelId, IProgress<double>? progress = null);

        /// <summary>
        /// Load a model into memory for inference
        /// </summary>
        Task LoadModelAsync(string modelId);

        /// <summary>
        /// Unload a model from memory
        /// </summary>
        Task UnloadModelAsync(string modelId);
    }
}