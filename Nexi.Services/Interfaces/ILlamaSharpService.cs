using LLama.Common;

namespace Nexi.Services.Interfaces
{
    public interface ILlamaSharpService
    {
        Task<bool> InitializeModelAsync(string modelPath, int contextSize, int gpuLayerCount);
        Task<string> GenerateResponseAsync(string prompt, Action<string> onTokenGenerated);
        Task<IAsyncEnumerable<string>> ChatAsync(string userMessage, ChatHistory? history = null);
        Task<bool> IsModelInitializedAsync();
        void Dispose();
    }
} 