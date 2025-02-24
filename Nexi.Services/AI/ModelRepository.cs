using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexi.Data.Context;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Nexi.Services.AI
{
    public class ModelRepository : IModelRepository
    {
        private readonly IDbContextFactory<NexiDbContext> _contextFactory;
        private readonly ILogger<ModelRepository> _logger;

        public ModelRepository(
            IDbContextFactory<NexiDbContext> contextFactory,
            ILogger<ModelRepository> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public async Task<IEnumerable<ModelInfo>> GetAvailableModelsAsync()
        {
            try
            {
                // First sync model data between ModelInfo and AIModelData
                await SyncModelsAsync();

                using var context = await _contextFactory.CreateDbContextAsync();
                return await context.ModelInfos.ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available models");
                return Enumerable.Empty<ModelInfo>();
            }
        }

        public async Task<ModelInfo> GetModelInfoAsync(string modelId)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.ModelInfos.FirstOrDefaultAsync(m => m.Id == modelId);
        }

        private async Task SyncModelsAsync()
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();

                // Get all model infos and AI model data
                var modelInfos = await context.ModelInfos.ToListAsync();
                var aiModels = await context.AIModels.ToListAsync();

                _logger.LogInformation("Syncing models. Found {ModelInfoCount} ModelInfo and {AIModelCount} AIModelData entries",
                    modelInfos.Count, aiModels.Count);

                // Ensure each ModelInfo has a corresponding AIModelData entry
                foreach (var modelInfo in modelInfos)
                {
                    var aiModel = aiModels.FirstOrDefault(m => m.Id == modelInfo.Id);

                    if (aiModel == null)
                    {
                        // Create new AIModelData for this ModelInfo
                        aiModel = new AIModelData
                        {
                            Id = modelInfo.Id,
                            Name = modelInfo.Name,
                            Description = modelInfo.Description,
                            Version = modelInfo.Version,
                            Size = modelInfo.Size,
                            Status = ModelStatus.NotDownloaded,
                            CreatedAt = DateTime.UtcNow,
                            LastModifiedAt = DateTime.UtcNow
                        };

                        context.AIModels.Add(aiModel);
                        _logger.LogInformation("Created new AIModelData for {ModelId}", modelInfo.Id);
                    }
                    else
                    {
                        // Update existing AIModelData with latest ModelInfo data
                        _logger.LogInformation("Updating existing ModelInfo: {ModelId}", modelInfo.Id);
                        aiModel = await context.AIModels.FindAsync(modelInfo.Id);

                        // Only update these fields if the model isn't already downloaded
                        if (aiModel.Status != ModelStatus.Downloaded)
                        {
                            aiModel.Name = modelInfo.Name;
                            aiModel.Description = modelInfo.Description;
                            aiModel.Version = modelInfo.Version;
                            aiModel.Size = modelInfo.Size;
                        }

                        aiModel.LastModifiedAt = DateTime.UtcNow;
                        _logger.LogInformation("Updating existing AIModelData: {ModelId}", modelInfo.Id);
                    }
                }

                // Ensure each AIModelData has a corresponding ModelInfo entry
                // (this might happen if models are added directly to the AIModels table)
                foreach (var aiModel in aiModels)
                {
                    var modelInfo = modelInfos.FirstOrDefault(m => m.Id == aiModel.Id);

                    if (modelInfo == null)
                    {
                        // Create new ModelInfo for this AIModelData
                        modelInfo = new ModelInfo
                        {
                            Id = aiModel.Id,
                            Name = aiModel.Name,
                            Description = aiModel.Description,
                            Version = aiModel.Version,
                            Size = aiModel.Size,
                            Provider = AIProvider.Local,
                            DownloadUrl = string.Empty,
                            CreatedAt = DateTime.UtcNow,
                            LastModifiedAt = DateTime.UtcNow
                        };

                        context.ModelInfos.Add(modelInfo);
                        _logger.LogInformation("Created new ModelInfo for {ModelId}", aiModel.Id);
                    }
                }

                // If there are no models at all, add some default ones
                if (!modelInfos.Any() && !aiModels.Any())
                {
                    await AddDefaultModelsAsync(context);
                }

                // Update timestamps to ensure sync is recorded
                var allModels = await context.AIModels.ToListAsync();
                var allInfos = await context.ModelInfos.ToListAsync();

                foreach (var model in allModels)
                {
                    model.LastModifiedAt = DateTime.UtcNow;
                }

                foreach (var info in allInfos)
                {
                    info.LastModifiedAt = DateTime.UtcNow;
                }

                await context.SaveChangesAsync();
                _logger.LogInformation("Model sync completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing models");
                throw;
            }
        }

        private async Task AddDefaultModelsAsync(NexiDbContext context)
        {
            _logger.LogInformation("Adding default models");

            // Add some common ONNX models
            var models = new List<ModelInfo>
            {
                new ModelInfo
                {
                    Id = "resnet50-v1-12",
                    Name = "ResNet-50 v1",
                    Description = "Image classification model trained on ImageNet",
                    Version = "1.0",
                    Size = "98 MB",
                    Provider = AIProvider.Local,
                    DownloadUrl = "https://github.com/onnx/models/raw/main/vision/classification/resnet/model/resnet50-v1-12.onnx",
                    SupportedTasks = new[] { "image-classification" },
                    CreatedAt = DateTime.UtcNow,
                    LastModifiedAt = DateTime.UtcNow
                },
                new ModelInfo
                {
                    Id = "bertsquad-10",
                    Name = "BERT SQuAD",
                    Description = "Question answering model based on BERT",
                    Version = "1.0",
                    Size = "435 MB",
                    Provider = AIProvider.Local,
                    DownloadUrl = "https://github.com/onnx/models/raw/main/text/machine_comprehension/bert-squad/model/bertsquad-10.onnx",
                    SupportedTasks = new[] { "question-answering" },
                    CreatedAt = DateTime.UtcNow,
                    LastModifiedAt = DateTime.UtcNow
                },
                new ModelInfo
                {
                    Id = "ssd-12",
                    Name = "SSD MobileNet v1",
                    Description = "Object detection model",
                    Version = "1.0",
                    Size = "67 MB",
                    Provider = AIProvider.Local,
                    DownloadUrl = "https://github.com/onnx/models/raw/main/vision/object_detection_segmentation/ssd-mobilenetv1/model/ssd_mobilenet_v1_12.onnx",
                    SupportedTasks = new[] { "object-detection" },
                    CreatedAt = DateTime.UtcNow,
                    LastModifiedAt = DateTime.UtcNow
                },
                new ModelInfo
                {
                    Id = "gpt2-10",
                    Name = "GPT-2",
                    Description = "Text generation model",
                    Version = "1.0",
                    Size = "548 MB",
                    Provider = AIProvider.Local,
                    DownloadUrl = "https://github.com/onnx/models/raw/main/text/machine_comprehension/gpt-2/model/gpt2-10.onnx",
                    SupportedTasks = new[] { "text-generation" },
                    CreatedAt = DateTime.UtcNow,
                    LastModifiedAt = DateTime.UtcNow
                }
            };

            foreach (var model in models)
            {
                context.ModelInfos.Add(model);

                // Also add the corresponding AIModelData
                context.AIModels.Add(new AIModelData
                {
                    Id = model.Id,
                    Name = model.Name,
                    Description = model.Description,
                    Version = model.Version,
                    Size = model.Size,
                    Status = ModelStatus.NotDownloaded,
                    CreatedAt = DateTime.UtcNow,
                    LastModifiedAt = DateTime.UtcNow
                });
            }

            await context.SaveChangesAsync();
            _logger.LogInformation("Added {Count} default models", models.Count);
        }
    }
}