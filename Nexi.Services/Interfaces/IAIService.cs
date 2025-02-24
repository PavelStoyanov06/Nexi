using Nexi.Data.Models;

namespace Nexi.Services.Interfaces
{
    public interface IAIService
    {
        event EventHandler<string> OnInferenceProgress;
        event EventHandler<Exception> OnError;

        Task<AIResponse> GetCompletionAsync(string prompt, AIRequestOptions options);
        Task<AIResponse> GetCompletionWithHistoryAsync(IEnumerable<(bool IsUser, string Message)> history, string prompt, AIRequestOptions options);
        Task DownloadModelAsync(string modelId, IProgress<double> progress);
        bool IsModelLoaded(string modelId);
        Task UnloadModelAsync(string modelId);
    }
}