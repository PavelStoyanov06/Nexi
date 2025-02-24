using Nexi.Data.Models;

namespace Nexi.Services.Interfaces
{
    public interface IModelRepository
    {
        Task<IEnumerable<ModelInfo>> GetAvailableModelsAsync();
        Task<string> GetModelDownloadUrlAsync(string modelId);
        Task<ModelInfo> GetModelInfoAsync(string modelId);
        Task UpdateModelStatusAsync(string modelId, ModelStatus status);
        Task AddModelAsync(ModelInfo model);
        Task UpdateModelAsync(ModelInfo model);
        Task RemoveModelAsync(string modelId);
    }
}