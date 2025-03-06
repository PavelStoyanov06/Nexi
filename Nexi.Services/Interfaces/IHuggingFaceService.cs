using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nexi.Services.Interfaces
{
    public interface IHuggingFaceService
    {
        Task<IEnumerable<HuggingFaceModelInfo>> SearchModelsAsync(string query);
        Task<IEnumerable<HuggingFaceModelFile>> GetModelFilesAsync(string modelId);
    }
} 