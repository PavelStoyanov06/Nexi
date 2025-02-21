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

        public async Task<bool> DeleteModelAsync(string id)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var model = await context.AIModels.FindAsync(id);
            if (model == null)
                return false;

            // Here you would add logic to delete the actual model files

            model.Status = ModelStatus.NotDownloaded;
            model.DownloadedDate = null;
            model.LocalPath = null;
            model.LastModifiedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();
            return true;
        }

        public async Task<AIModelData> StartDownloadModelAsync(string id)
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
    }
}