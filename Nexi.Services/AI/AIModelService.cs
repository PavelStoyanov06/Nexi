using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexi.Data.Context;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;

namespace Nexi.Services.AI
{
    public class AIModelService : IAIModelService
    {
        private readonly IDbContextFactory<NexiDbContext> _contextFactory;
        private readonly ILogger<AIModelService> _logger;
        private readonly string _modelsBasePath;

        public AIModelService(
            IDbContextFactory<NexiDbContext> contextFactory,
            ILogger<AIModelService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;

            // Set up the models directory in the user's app data folder
            _modelsBasePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Nexi", "Models");

            // Ensure directory exists
            Directory.CreateDirectory(_modelsBasePath);
        }

        public async Task<IEnumerable<AIModelData>> GetAllModelsAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.AIModels
                .OrderBy(m => m.Name)
                .ToListAsync();
        }

        public async Task<AIModelData?> GetModelAsync(string id)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.AIModels.FindAsync(id);
        }

        public async Task<AIModelData> UpdateModelStatusAsync(string id, ModelStatus status)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var model = await context.AIModels.FindAsync(id);
            if (model == null)
                throw new KeyNotFoundException($"Model {id} not found");

            model.Status = status;
            model.LastModifiedAt = DateTime.UtcNow;

            if (status == ModelStatus.Downloaded && !model.DownloadedDate.HasValue)
                model.DownloadedDate = DateTime.UtcNow;

            await context.SaveChangesAsync();
            return model;
        }

        public async Task<AIModelData> UpdateModelAsync(string id, string localPath, ModelStatus status)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var model = await context.AIModels.FindAsync(id);
            if (model == null)
                throw new KeyNotFoundException($"Model {id} not found");

            model.Status = status;
            model.LastModifiedAt = DateTime.UtcNow;
            model.LocalPath = localPath;

            if (status == ModelStatus.Downloaded && !model.DownloadedDate.HasValue)
                model.DownloadedDate = DateTime.UtcNow;

            await context.SaveChangesAsync();
            return model;
        }

        public async Task<bool> DeleteModelAsync(string id)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var model = await context.AIModels.FindAsync(id);
            if (model == null)
                return false;

            try
            {
                // Delete model files if they exist
                if (!string.IsNullOrEmpty(model.LocalPath) && File.Exists(model.LocalPath))
                {
                    File.Delete(model.LocalPath);

                    // Check if there's a directory with model files
                    var modelDir = Path.Combine(_modelsBasePath, id);
                    if (Directory.Exists(modelDir))
                    {
                        Directory.Delete(modelDir, true);
                    }
                }

                // Update model status to not downloaded
                model.Status = ModelStatus.NotDownloaded;
                model.DownloadedDate = null;
                model.LocalPath = null;
                model.LastModifiedAt = DateTime.UtcNow;

                await context.SaveChangesAsync();
                return true;
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "IO error deleting model files for {ModelId}", id);
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Access denied when deleting model files for {ModelId}", id);
                return false;
            }
        }

        public async Task<IEnumerable<AIModelData>> GetModelsBatchAsync(IEnumerable<string> ids)
        {
            using var context = await _contextFactory.CreateDbContextAsync();

            // Create a hash set of IDs for better performance
            var idSet = new HashSet<string>(ids);

            // Query models that match any ID in the set
            var models = await context.AIModels
                .Where(m => idSet.Contains(m.Id))
                .ToListAsync();

            return models;
        }


        public async Task<UserSettings> GetUserSettingsAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var settings = await context.UserSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                // Create default settings if none exist
                settings = new UserSettings
                {
                    UseGPU = false,
                    InputSensitivity = 50,
                    SelectedTheme = ThemeMode.System,
                    UseSystemAccent = true,
                    AccentColor = "#A880E4",
                    LastModifiedAt = DateTime.UtcNow
                };
                context.UserSettings.Add(settings);
                await context.SaveChangesAsync();
            }
            return settings;
        }

        public Task<bool> IsQuantizedVersionAvailableAsync(string modelId)
        {
            // For this implementation, we'll assume all models have quantized versions available
            return Task.FromResult(true);
        }

        public Task<IEnumerable<string>> GetSupportedQuantizationLevelsAsync(string modelId)
        {
            // Common quantization levels for GGUF/GGML models
            var levels = new List<string>
            {
                "Q4_0", // Fastest, lowest quality
                "Q4_K_M", // Good balance of speed and quality
                "Q5_K_M", // Better quality, slightly slower
                "Q6_K", // High quality, slower
                "Q8_0" // Highest quality, slowest
            };

            return Task.FromResult<IEnumerable<string>>(levels);
        }
    }
}