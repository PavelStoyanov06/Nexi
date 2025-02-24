using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexi.Data.Context;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;

namespace Nexi.Services
{
    public class AIModelService : IAIModelService
    {
        private readonly IDbContextFactory<NexiDbContext> _contextFactory;
        private readonly ILogger<AIModelService> _logger;

        public AIModelService(IDbContextFactory<NexiDbContext> contextFactory, ILogger<AIModelService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public async Task<IEnumerable<AIModelData>> GetAllModelsAsync()
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                return await context.AIModels
                    .OrderBy(m => m.Name)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all models");
                return Enumerable.Empty<AIModelData>();
            }
        }

        public async Task<AIModelData?> GetModelAsync(string id)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var model = await context.AIModels.FindAsync(id);

                // Verify the model file exists and has the correct status
                if (model != null && model.Status == ModelStatus.Downloaded &&
                    !string.IsNullOrEmpty(model.LocalPath))
                {
                    if (!File.Exists(model.LocalPath))
                    {
                        _logger.LogWarning("Model {Id} is marked as downloaded but file {Path} doesn't exist",
                            id, model.LocalPath);

                        // Update status to NotDownloaded
                        model.Status = ModelStatus.NotDownloaded;
                        model.LocalPath = null;
                        await context.SaveChangesAsync();
                    }
                    else
                    {
                        // Check file size - ONNX models shouldn't be tiny
                        var fileInfo = new FileInfo(model.LocalPath);
                        if (fileInfo.Length < 10240) // Less than 10KB
                        {
                            _logger.LogWarning("Model {Id} file is suspiciously small: {Size} bytes",
                                id, fileInfo.Length);
                        }
                    }
                }

                return model;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting model {Id}", id);
                return null;
            }
        }

        public async Task<AIModelData> UpdateModelStatusAsync(string id, ModelStatus status)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var model = await context.AIModels.FindAsync(id);
                if (model == null)
                    throw new KeyNotFoundException($"Model {id} not found");

                model.Status = status;
                model.LastModifiedAt = DateTime.UtcNow;

                if (status == ModelStatus.Downloaded && !model.DownloadedDate.HasValue)
                    model.DownloadedDate = DateTime.UtcNow;

                // If status is Error or NotDownloaded, clear the LocalPath
                if (status == ModelStatus.Error || status == ModelStatus.NotDownloaded)
                {
                    model.LocalPath = null;
                }

                await context.SaveChangesAsync();
                return model;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating model status for {Id}", id);
                throw;
            }
        }

        public async Task<bool> DeleteModelAsync(string id)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var model = await context.AIModels.FindAsync(id);
                if (model == null)
                    return false;

                // Delete the actual model files if they exist
                if (!string.IsNullOrEmpty(model.LocalPath) && File.Exists(model.LocalPath))
                {
                    try
                    {
                        File.Delete(model.LocalPath);
                        _logger.LogInformation("Deleted model file: {Path}", model.LocalPath);

                        // Also try to delete the parent directory if it's empty
                        var directory = Path.GetDirectoryName(model.LocalPath);
                        if (directory != null && Directory.Exists(directory) &&
                            !Directory.EnumerateFileSystemEntries(directory).Any())
                        {
                            Directory.Delete(directory);
                            _logger.LogInformation("Deleted empty model directory: {Path}", directory);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error deleting model file: {Path}", model.LocalPath);
                    }
                }

                model.Status = ModelStatus.NotDownloaded;
                model.DownloadedDate = null;
                model.LocalPath = null;
                model.LastModifiedAt = DateTime.UtcNow;

                await context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting model {Id}", id);
                return false;
            }
        }

        public async Task<AIModelData> StartDownloadModelAsync(string id)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var model = await context.AIModels.FindAsync(id);
                if (model == null)
                    throw new KeyNotFoundException($"Model {id} not found");

                // Set status to downloading
                model.Status = ModelStatus.Downloading;
                model.LastModifiedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();

                // Here you would initiate a real download process, 
                // perhaps using a background service

                return model;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting model download for {Id}", id);
                throw;
            }
        }

        public async Task<bool> VerifyAllModelsAsync()
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var models = await context.AIModels.ToListAsync();
                bool anyFixed = false;

                foreach (var model in models)
                {
                    if (model.Status == ModelStatus.Downloaded && !string.IsNullOrEmpty(model.LocalPath))
                    {
                        // Check if file exists
                        if (!File.Exists(model.LocalPath))
                        {
                            _logger.LogWarning("Model {Id} file {Path} doesn't exist. Updating status.",
                                model.Id, model.LocalPath);

                            model.Status = ModelStatus.NotDownloaded;
                            model.LocalPath = null;
                            anyFixed = true;
                        }
                        else
                        {
                            // Check file size
                            var fileInfo = new FileInfo(model.LocalPath);
                            if (fileInfo.Length < 10240) // Less than 10KB
                            {
                                _logger.LogWarning("Model {Id} file is too small: {Size} bytes. Marking as invalid.",
                                    model.Id, fileInfo.Length);

                                model.Status = ModelStatus.Error;
                                anyFixed = true;
                            }
                        }
                    }
                }

                if (anyFixed)
                {
                    await context.SaveChangesAsync();
                    _logger.LogInformation("Fixed inconsistent model statuses during verification");
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying models");
                return false;
            }
        }
    }
}