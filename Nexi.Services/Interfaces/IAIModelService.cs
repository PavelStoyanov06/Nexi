using Nexi.Data.Models;

namespace Nexi.Services.Interfaces
{
    public interface IAIModelService
    {
        Task<IEnumerable<AIModelData>> GetAllModelsAsync();
        Task<AIModelData?> GetModelAsync(string id);
        Task<AIModelData> UpdateModelStatusAsync(string id, ModelStatus status);
        Task<bool> DeleteModelAsync(string id);
        Task<AIModelData> StartDownloadModelAsync(string id);
        
        // New methods for HuggingFace model downloading
        Task<AIModelData> DownloadModelFromHuggingFaceAsync(string id, string repoId, IProgress<(string, int)>? progress = null);
        Task<bool> IsModelDownloadedAsync(string id);
        Task<string> GetModelLocalPathAsync(string id);
        Task<bool> RunModelInferenceAsync(string id, string prompt, Action<string> onTokenGenerated);
        
        // HuggingFace GGUF model methods
        Task<IEnumerable<HuggingFaceModelInfo>> SearchHuggingFaceModelsAsync(string query);
        Task<IEnumerable<HuggingFaceModelFile>> GetModelFilesAsync(string modelId);
        Task<AIModelData> DownloadGgufModelAsync(string modelId, string fileName, IProgress<(string, int)>? progress = null);
        bool CancelDownload(string modelId);
    }
}

public class HuggingFaceModelInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<HuggingFaceModelFile> Files { get; set; } = new List<HuggingFaceModelFile>();
}

public class HuggingFaceModelFile
{
    public string FileName { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public long SizeInBytes { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public string Quantization { get; set; } = string.Empty;
}