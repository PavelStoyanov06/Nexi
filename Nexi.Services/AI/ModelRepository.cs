using Microsoft.Extensions.Logging;
using Nexi.Data.Context;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Nexi.Services.AI
{
    public class ModelRepository : IModelRepository
    {
        private readonly IDbContextFactory<NexiDbContext> _contextFactory;
        private readonly ILogger<ModelRepository> _logger;
        private readonly Dictionary<string, ModelInfo> _currentModels;

        public ModelRepository(IDbContextFactory<NexiDbContext> contextFactory, ILogger<ModelRepository> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
            _currentModels = GetOnnxModelZooModels();

            // Ensure models are synced with current version
            _ = SyncModelsAsync();
        }

        private Dictionary<string, ModelInfo> GetOnnxModelZooModels()
        {
            // These models are from the ONNX Model Zoo and don't require authentication
            return new Dictionary<string, ModelInfo>
            {
                ["resnet50-v1-12"] = new ModelInfo
                {
                    Id = "resnet50-v1-12",
                    Name = "ResNet-50 v1",
                    Description = "Image classification model for detecting objects in images",
                    DownloadUrl = "https://github.com/onnx/models/raw/main/validated/vision/classification/resnet/model/resnet50-v1-12.onnx",
                    Size = "98MB",
                    Version = "1.0.0",
                    SupportedTasks = new[] { "image-classification" },
                    Provider = AIProvider.Local,
                    MetadataJson = JsonSerializer.Serialize(new Dictionary<string, string>
                    {
                        ["architecture"] = "CNN",
                        ["license"] = "MIT",
                        ["domain"] = "vision"
                    }),
                    CreatedAt = DateTime.UtcNow,
                    LastModifiedAt = DateTime.UtcNow
                },
                ["bertsquad-10"] = new ModelInfo
                {
                    Id = "bertsquad-10",
                    Name = "BERT-SQuAD",
                    Description = "Question answering model based on BERT",
                    DownloadUrl = "https://github.com/onnx/models/raw/main/validated/text/machine_comprehension/bert-squad/model/bertsquad-10.onnx",
                    Size = "438MB",
                    Version = "1.0.0",
                    SupportedTasks = new[] { "question-answering" },
                    Provider = AIProvider.Local,
                    MetadataJson = JsonSerializer.Serialize(new Dictionary<string, string>
                    {
                        ["architecture"] = "transformer",
                        ["license"] = "Apache-2.0",
                        ["domain"] = "text"
                    }),
                    CreatedAt = DateTime.UtcNow,
                    LastModifiedAt = DateTime.UtcNow
                },
                ["ssd-12"] = new ModelInfo
                {
                    Id = "ssd-12",
                    Name = "SSD Model",
                    Description = "Single Shot MultiBox Detector for object detection",
                    DownloadUrl = "https://github.com/onnx/models/raw/main/validated/vision/object_detection_segmentation/ssd/model/ssd-12.onnx",
                    Size = "77MB",
                    Version = "1.0.0",
                    SupportedTasks = new[] { "object-detection" },
                    Provider = AIProvider.Local,
                    MetadataJson = JsonSerializer.Serialize(new Dictionary<string, string>
                    {
                        ["architecture"] = "SSD",
                        ["license"] = "MIT",
                        ["domain"] = "vision"
                    }),
                    CreatedAt = DateTime.UtcNow,
                    LastModifiedAt = DateTime.UtcNow
                },
                ["gpt2-10"] = new ModelInfo
                {
                    Id = "gpt2-10",
                    Name = "GPT-2",
                    Description = "Text generation and completion model",
                    DownloadUrl = "https://github.com/onnx/models/raw/main/validated/text/machine_comprehension/gpt-2/model/gpt2-10.onnx",
                    Size = "548MB",
                    Version = "1.0.0",
                    SupportedTasks = new[] { "text-generation", "completion" },
                    Provider = AIProvider.Local,
                    MetadataJson = JsonSerializer.Serialize(new Dictionary<string, string>
                    {
                        ["architecture"] = "transformer",
                        ["license"] = "MIT",
                        ["domain"] = "text"
                    }),
                    CreatedAt = DateTime.UtcNow,
                    LastModifiedAt = DateTime.UtcNow
                }
            };
        }

        private async Task SyncModelsAsync()
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();

                // Get existing models from database
                var existingModelInfos = await context.ModelInfos.ToListAsync();
                var existingAiModels = await context.AIModels.ToListAsync();

                _logger.LogInformation("Syncing models. Found {ModelInfoCount} ModelInfo and {AIModelCount} AIModelData entries",
                    existingModelInfos.Count, existingAiModels.Count);

                // Remove obsolete models (not in the current models dictionary)
                foreach (var modelInfo in existingModelInfos)
                {
                    if (!_currentModels.ContainsKey(modelInfo.Id))
                    {
                        context.ModelInfos.Remove(modelInfo);
                        _logger.LogInformation("Removing obsolete ModelInfo: {ModelId}", modelInfo.Id);
                    }
                }

                foreach (var aiModel in existingAiModels)
                {
                    if (!_currentModels.ContainsKey(aiModel.Id))
                    {
                        context.AIModels.Remove(aiModel);
                        _logger.LogInformation("Removing obsolete AIModelData: {ModelId}", aiModel.Id);
                    }
                }

                // Add new models and update existing ones
                foreach (var (id, currentModel) in _currentModels)
                {
                    // Check if ModelInfo exists
                    var existingModelInfo = existingModelInfos.FirstOrDefault(m => m.Id == id);
                    if (existingModelInfo == null)
                    {
                        // Add new ModelInfo
                        context.ModelInfos.Add(currentModel);
                        _logger.LogInformation("Adding new ModelInfo: {ModelId}", id);
                    }
                    else
                    {
                        // Update existing ModelInfo
                        existingModelInfo.Name = currentModel.Name;
                        existingModelInfo.Description = currentModel.Description;
                        existingModelInfo.DownloadUrl = currentModel.DownloadUrl;
                        existingModelInfo.Size = currentModel.Size;
                        existingModelInfo.Version = currentModel.Version;
                        existingModelInfo.SupportedTasksJson = string.Join(",", currentModel.SupportedTasks);
                        existingModelInfo.MetadataJson = currentModel.MetadataJson;
                        existingModelInfo.LastModifiedAt = DateTime.UtcNow;
                        _logger.LogInformation("Updating existing ModelInfo: {ModelId}", id);
                    }

                    // Check if AIModelData exists
                    var existingAiModel = existingAiModels.FirstOrDefault(m => m.Id == id);
                    if (existingAiModel == null)
                    {
                        // Add new AIModelData
                        var aiModelData = new AIModelData
                        {
                            Id = id,
                            Name = currentModel.Name,
                            Description = currentModel.Description,
                            Status = ModelStatus.NotDownloaded,
                            Size = currentModel.Size,
                            Version = currentModel.Version,
                            CreatedAt = DateTime.UtcNow,
                            LastModifiedAt = DateTime.UtcNow
                        };

                        context.AIModels.Add(aiModelData);
                        _logger.LogInformation("Adding new AIModelData: {ModelId}", id);
                    }
                    else
                    {
                        // Update AIModelData but preserve status
                        existingAiModel.Name = currentModel.Name;
                        existingAiModel.Description = currentModel.Description;
                        existingAiModel.Size = currentModel.Size;
                        existingAiModel.Version = currentModel.Version;
                        existingAiModel.LastModifiedAt = DateTime.UtcNow;
                        _logger.LogInformation("Updating existing AIModelData: {ModelId}", id);
                    }
                }

                // Save changes
                await context.SaveChangesAsync();
                _logger.LogInformation("Model sync completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing models");
            }
        }

        public async Task<IEnumerable<ModelInfo>> GetAvailableModelsAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.ModelInfos.ToListAsync();
        }

        public async Task<string> GetModelDownloadUrlAsync(string modelId)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var model = await context.ModelInfos.FindAsync(modelId);
            if (model != null)
            {
                return model.DownloadUrl;
            }
            throw new KeyNotFoundException($"Model {modelId} not found in database");
        }

        public async Task<ModelInfo> GetModelInfoAsync(string modelId)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var model = await context.ModelInfos.FindAsync(modelId);
            if (model != null)
            {
                // Parse metadata JSON
                if (!string.IsNullOrEmpty(model.MetadataJson))
                {
                    try
                    {
                        model.Metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(model.MetadataJson)
                            ?? new Dictionary<string, string>();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error deserializing metadata for model {ModelId}", modelId);
                    }
                }

                return model;
            }
            throw new KeyNotFoundException($"Model {modelId} not found in database");
        }

        public async Task UpdateModelStatusAsync(string modelId, ModelStatus status)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var model = await context.AIModels.FindAsync(modelId);
            if (model != null)
            {
                model.Status = status;
                model.LastModifiedAt = DateTime.UtcNow;
                if (status == ModelStatus.Downloaded)
                {
                    model.DownloadedDate = DateTime.UtcNow;
                }
                await context.SaveChangesAsync();
            }
            else
            {
                // If model doesn't exist yet, create it based on current models
                if (_currentModels.TryGetValue(modelId, out var currentModel))
                {
                    var aiModelData = new AIModelData
                    {
                        Id = modelId,
                        Name = currentModel.Name,
                        Description = currentModel.Description,
                        Status = status,
                        Size = currentModel.Size,
                        Version = currentModel.Version,
                        CreatedAt = DateTime.UtcNow,
                        LastModifiedAt = DateTime.UtcNow
                    };

                    if (status == ModelStatus.Downloaded)
                    {
                        aiModelData.DownloadedDate = DateTime.UtcNow;
                    }

                    context.AIModels.Add(aiModelData);
                    await context.SaveChangesAsync();
                }
                else
                {
                    throw new KeyNotFoundException($"Model {modelId} not found in database or current models");
                }
            }
        }

        public async Task AddModelAsync(ModelInfo model)
        {
            using var context = await _contextFactory.CreateDbContextAsync();

            // Serialize metadata if needed
            if (model.Metadata.Count > 0 && string.IsNullOrEmpty(model.MetadataJson))
            {
                model.MetadataJson = JsonSerializer.Serialize(model.Metadata);
            }

            model.CreatedAt = DateTime.UtcNow;
            model.LastModifiedAt = DateTime.UtcNow;

            // Create a matching AIModelData entry
            var aiModelData = new AIModelData
            {
                Id = model.Id,
                Name = model.Name,
                Description = model.Description,
                Status = ModelStatus.NotDownloaded,
                Size = model.Size,
                Version = model.Version,
                CreatedAt = DateTime.UtcNow,
                LastModifiedAt = DateTime.UtcNow
            };

            // Add both entries
            context.ModelInfos.Add(model);

            if (!await context.AIModels.AnyAsync(m => m.Id == model.Id))
            {
                context.AIModels.Add(aiModelData);
            }

            await context.SaveChangesAsync();

            // Add to current models dictionary
            _currentModels[model.Id] = model;
        }

        public async Task UpdateModelAsync(ModelInfo model)
        {
            using var context = await _contextFactory.CreateDbContextAsync();

            // Serialize metadata if needed
            if (model.Metadata.Count > 0)
            {
                model.MetadataJson = JsonSerializer.Serialize(model.Metadata);
            }

            model.LastModifiedAt = DateTime.UtcNow;

            context.ModelInfos.Update(model);

            // Update the corresponding AIModelData entry
            var aiModelData = await context.AIModels.FindAsync(model.Id);
            if (aiModelData != null)
            {
                aiModelData.Name = model.Name;
                aiModelData.Description = model.Description;
                aiModelData.Size = model.Size;
                aiModelData.Version = model.Version;
                aiModelData.LastModifiedAt = DateTime.UtcNow;
            }

            await context.SaveChangesAsync();

            // Update in current models dictionary
            _currentModels[model.Id] = model;
        }

        public async Task RemoveModelAsync(string modelId)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var modelInfo = await context.ModelInfos.FindAsync(modelId);
            if (modelInfo != null)
            {
                context.ModelInfos.Remove(modelInfo);
            }

            var aiModelData = await context.AIModels.FindAsync(modelId);
            if (aiModelData != null)
            {
                context.AIModels.Remove(aiModelData);
            }

            await context.SaveChangesAsync();

            // Remove from current models dictionary if present
            _currentModels.Remove(modelId);
        }
    }
}