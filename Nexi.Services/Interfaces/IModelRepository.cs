using Nexi.Data.Models;

namespace Nexi.Services.Interfaces
{
    public interface IModelRepository
    {
        Task<IEnumerable<ModelInfo>> GetAvailableModelsAsync();
        Task<ModelInfo> GetModelInfoAsync(string modelId);
    }
}