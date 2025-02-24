using Nexi.Data.Models;

namespace Nexi.Services.Interfaces
{
    public interface IAIService
    {
        // Core inference methods
        Task<AIResponse> GetCompletionAsync(string prompt, AIRequestOptions? options = null);
        Task<AIResponse> GetCompletionWithHistoryAsync(IEnumerable<(bool isUser, string message)> history, string prompt, AIRequestOptions? options = null);

        // Embeddings for semantic search
        Task<float[]> GetEmbeddingsAsync(string text);

        // Model management
        Task LoadModelAsync(string modelId);
        Task UnloadModelAsync(string modelId);
        bool IsModelLoaded(string modelId);
        Task DownloadModelAsync(string modelId, IProgress<double>? progress = null);

        // Settings
        Task UpdateSettingsAsync(AIRequestOptions options);
        AIRequestOptions GetCurrentSettings();

        // Events for progress and error reporting
        event EventHandler<string> OnInferenceProgress;
        event EventHandler<Exception> OnError;
    }
}