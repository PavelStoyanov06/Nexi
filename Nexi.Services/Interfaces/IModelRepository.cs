using Nexi.Data.Models;

namespace Nexi.Services.Interfaces
{
    public interface IModelRepository
    {
        /// <summary>
        /// Get all available models from the repository
        /// </summary>
        Task<IEnumerable<ModelInfo>> GetAvailableModelsAsync();

        /// <summary>
        /// Get detailed model information for a specific model
        /// </summary>
        Task<ModelInfo?> GetModelInfoAsync(string id);

        /// <summary>
        /// Refresh the model catalog from the remote source
        /// </summary>
        Task RefreshModelCatalogAsync();
    }
}