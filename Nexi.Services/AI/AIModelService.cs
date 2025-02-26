using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexi.Data.Context;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Nexi.Services
{
    public class AIModelService : IAIModelService
    {
        private readonly IDbContextFactory<NexiDbContext> _contextFactory;
        private readonly ILogger<AIModelService> _logger;
        private readonly IModelRepository _modelRepository;
        private readonly string _modelsBasePath;

        public AIModelService(
            IDbContextFactory<NexiDbContext> contextFactory,
            ILogger<AIModelService> logger,
            IModelRepository modelRepository)
        {
            _contextFactory = contextFactory;
            _logger = logger;
            _modelRepository = modelRepository;

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

            // First, ensure we have the latest model info in the database
            await SyncModelsWithRepositoryAsync();

            return await context.AIModels
                .OrderBy(m => m.Name)
                .ToListAsync();
        }

        public async Task<AIModelData?> GetModelAsync(string id)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.AIModels.FindAsync(id);
        }

        public async Task<ModelInfo?> GetModelInfoAsync(string id)
        {
            return await _modelRepository.GetModelInfoAsync(id);
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting model files for {ModelId}", id);
                return false;
            }
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

        public async Task<bool> IsQuantizedVersionAvailableAsync(string modelId)
        {
            // For this implementation, we'll assume all models have quantized versions available
            return await Task.FromResult(true);
        }

        public async Task<IEnumerable<string>> GetSupportedQuantizationLevelsAsync(string modelId)
        {
            // Common quantization levels for GGUF/GGML models
            return await Task.FromResult(new List<string>
            {
                "Q4_0", // Fastest, lowest quality
                "Q4_K_M", // Good balance of speed and quality
                "Q5_K_M", // Better quality, slightly slower
                "Q6_K", // High quality, slower
                "Q8_0" // Highest quality, slowest
            });
        }

        private async Task SyncModelsWithRepositoryAsync()
        {
            try
            {
                // Get models from repository
                var repoModels = await _modelRepository.GetAvailableModelsAsync();

                using var context = await _contextFactory.CreateDbContextAsync();
                var dbModels = await context.AIModels.ToListAsync();

                // Add new models that are in the repository but not in the database
                foreach (var repoModel in repoModels)
                {
                    if (!dbModels.Any(m => m.Id == repoModel.Id))
                    {
                        var newModel = new AIModelData
                        {
                            Id = repoModel.Id,
                            Name = repoModel.Name,
                            Description = repoModel.Description,
                            Size = repoModel.Size,
                            Version = repoModel.Version,
                            Status = ModelStatus.NotDownloaded,
                            CreatedAt = DateTime.UtcNow,
                            LastModifiedAt = DateTime.UtcNow
                        };

                        context.AIModels.Add(newModel);
                        _logger.LogInformation("Added new model: {ModelName} ({ModelId})", newModel.Name, newModel.Id);
                    }
                }

                // Update existing models with latest info from repository
                foreach (var dbModel in dbModels)
                {
                    var repoModel = repoModels.FirstOrDefault(m => m.Id == dbModel.Id);
                    if (repoModel != null)
                    {
                        // Keep status and local path, update metadata
                        dbModel.Name = repoModel.Name;
                        dbModel.Description = repoModel.Description;
                        dbModel.Size = repoModel.Size;
                        dbModel.Version = repoModel.Version;
                        dbModel.LastModifiedAt = DateTime.UtcNow;
                    }
                }

                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing models with repository");
            }
        }
    }
}