using Nexi.Data.Models;

namespace Nexi.Services.Interfaces
{
    public interface IAIModelService
    {
        /// <summary>
        /// Get all AI models from the database
        /// </summary>
        Task<IEnumerable<AIModelData>> GetAllModelsAsync();

        /// <summary>
        /// Get a specific AI model by ID
        /// </summary>
        Task<AIModelData?> GetModelAsync(string id);

        /// <summary>
        /// Get detailed model information including download URL
        /// </summary>
        Task<ModelInfo?> GetModelInfoAsync(string id);

        /// <summary>
        /// Update model status (Downloaded, Downloading, Error)
        /// </summary>
        Task<AIModelData> UpdateModelStatusAsync(string id, ModelStatus status);

        /// <summary>
        /// Update model with local file path and status
        /// </summary>
        Task<AIModelData> UpdateModelAsync(string id, string localPath, ModelStatus status);

        /// <summary>
        /// Delete a model's files and update its status
        /// </summary>
        Task<bool> DeleteModelAsync(string id);

        /// <summary>
        /// Get user settings for AI configuration
        /// </summary>
        Task<UserSettings> GetUserSettingsAsync();

        /// <summary>
        /// Check if a quantized version of a model is available
        /// </summary>
        Task<bool> IsQuantizedVersionAvailableAsync(string modelId);

        /// <summary>
        /// Get the list of supported quantization levels for a model
        /// </summary>
        Task<IEnumerable<string>> GetSupportedQuantizationLevelsAsync(string modelId);
    }
}