using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexi.Data.Context;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;

namespace Nexi.Services
{
    public class AIModelService : IAIModelService
    {
        private readonly NexiDbContext _context;
        private readonly ILogger<AIModelService> _logger;

        public AIModelService(NexiDbContext context, ILogger<AIModelService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IEnumerable<AIModelData>> GetAllModelsAsync()
        {
            return await _context.AIModels
                .OrderBy(m => m.Name)
                .ToListAsync();
        }

        public async Task<AIModelData?> GetModelAsync(string id)
        {
            return await _context.AIModels.FindAsync(id);
        }

        public async Task<AIModelData> UpdateModelStatusAsync(string id, ModelStatus status)
        {
            var model = await _context.AIModels.FindAsync(id);
            if (model == null)
                throw new KeyNotFoundException($"Model {id} not found");

            model.Status = status;
            model.LastModifiedAt = DateTime.UtcNow;

            if (status == ModelStatus.Downloaded && !model.DownloadedDate.HasValue)
                model.DownloadedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return model;
        }

        public async Task<bool> DeleteModelAsync(string id)
        {
            var model = await _context.AIModels.FindAsync(id);
            if (model == null)
                return false;

            // Here you would add logic to delete the actual model files

            _context.AIModels.Remove(model);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<AIModelData> StartDownloadModelAsync(string id)
        {
            var model = await _context.AIModels.FindAsync(id);
            if (model == null)
                throw new KeyNotFoundException($"Model {id} not found");

            // Set status to downloading
            model.Status = ModelStatus.Downloading;
            model.LastModifiedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Here you would initiate a real download process, 
            // perhaps using a background service

            return model;
        }
    }
}