using Interfaces;
using Microsoft.Extensions.Logging;
using Nexi.Data.Models;
using System.Threading.Tasks;

namespace Nexi.Services.Interfaces
{
    public interface IModelRepository
    {
        /// <summary>
        /// Get all available models from the repository
        /// </summary>
        Task<IEnumerable<AIModelData>> GetAvailableModelsAsync();

        /// <summary>
        /// Get detailed model information for a specific model
        /// </summary>
        Task<AIModelData> GetModelInfoAsync(string id);

        /// <summary>
        /// Refresh the model catalog from the remote source
        /// </summary>
        Task RefreshModelCatalogAsync();

        /// <summary>
        /// Downloads a model that requires authentication using the appropriate token
        /// </summary>
        Task<byte[]> DownloadModelWithAuthAsync(
            string modelUrl,
            string provider,
            string modelName,
            IAuthenticationService authService,
        ILogger logger);

        Task <IEnumerable<AIModelData>> GetModelsPageAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    }
}