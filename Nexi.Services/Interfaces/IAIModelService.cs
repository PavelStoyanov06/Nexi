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
    }
}