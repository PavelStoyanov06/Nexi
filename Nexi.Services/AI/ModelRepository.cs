using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexi.Data.Context;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;
using System.Text.Json;
using System.Net.Http;

namespace Nexi.Services
{
    public class ModelRepository : IModelRepository
    {
        private readonly IDbContextFactory<NexiDbContext> _contextFactory;
        private readonly ILogger<ModelRepository> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _modelCachePath;
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(12);

        // Model Repository URLs - using Microsoft ONNX Model Zoo and other public sources
        private const string ONNX_MODEL_ZOO = "https://github.com/onnx/models/tree/main/";

        public ModelRepository(
            IDbContextFactory<NexiDbContext> contextFactory,
            ILogger<ModelRepository> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36");

            _modelCachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Nexi", "OnnxModelCache.json");

            // Ensure the directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(_modelCachePath));
        }

        public async Task<IEnumerable<ModelInfo>> GetAvailableModelsAsync()
        {
            try
            {
                // Check if we have a cached response that's not expired
                if (TryGetCachedModels(out var cachedModels))
                {
                    _logger.LogInformation("Returning {Count} cached ONNX models", cachedModels.Count());
                    return cachedModels;
                }

                // Use our built-in reliable models
                var models = GetBuiltInOnnxModels();

                // Save models to database
                await SaveModelsToDbAsync(models);

                // Cache the models
                await CacheModelsAsync(models);

                _logger.LogInformation("Returning {Count} ONNX models", models.Count);
                return models;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available ONNX models");

                // Fall back to built-in models in case of failure
                return GetBuiltInOnnxModels();
            }
        }

        public async Task<ModelInfo?> GetModelInfoAsync(string id)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                return await context.ModelInfos.FindAsync(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting model info for {ModelId}", id);

                // Try to find the model in our built-in list
                return GetBuiltInOnnxModels().FirstOrDefault(m => m.Id == id);
            }
        }

        public async Task RefreshModelCatalogAsync()
        {
            try
            {
                // Clear cache
                if (File.Exists(_modelCachePath))
                {
                    File.Delete(_modelCachePath);
                }

                // Fetch fresh models
                var models = await GetAvailableModelsAsync();
                _logger.LogInformation("Refreshed ONNX model catalog with {Count} models", models.Count());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing ONNX model catalog");
            }
        }

        private async Task SaveModelsToDbAsync(IEnumerable<ModelInfo> models)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();

                // Clear existing models to ensure we have the latest information
                var existingModels = await context.ModelInfos.ToListAsync();
                if (existingModels.Any())
                {
                    context.ModelInfos.RemoveRange(existingModels);
                    await context.SaveChangesAsync();
                    _logger.LogInformation("Cleared {Count} existing models from database", existingModels.Count);
                }

                // Add all models
                context.ModelInfos.AddRange(models);
                await context.SaveChangesAsync();
                _logger.LogInformation("Added {Count} ONNX models to database", models.Count());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving ONNX models to database");
            }
        }

        private bool TryGetCachedModels(out IEnumerable<ModelInfo> models)
        {
            models = null;

            try
            {
                if (!File.Exists(_modelCachePath))
                    return false;

                var fileInfo = new FileInfo(_modelCachePath);
                if (DateTime.UtcNow - fileInfo.LastWriteTimeUtc > _cacheExpiration)
                    return false;

                string json = File.ReadAllText(_modelCachePath);
                models = JsonSerializer.Deserialize<List<ModelInfo>>(json);
                return models != null && models.Any();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reading ONNX model cache");
                return false;
            }
        }

        private async Task CacheModelsAsync(IEnumerable<ModelInfo> models)
        {
            try
            {
                string json = JsonSerializer.Serialize(models, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(_modelCachePath, json);
                _logger.LogInformation("Cached {Count} ONNX models to {FilePath}", models.Count(), _modelCachePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error caching ONNX models");
            }
        }

        private List<ModelInfo> GetBuiltInOnnxModels()
        {
            // Essential ONNX models using reliable download URLs from Microsoft ONNX Model Zoo and ONNX Runtime GitHub
            return new List<ModelInfo>
            {
                new ModelInfo
                {
                    Id = "onnx-bert-base-uncased",
                    Name = "BERT Base Uncased",
                    Description = "BERT base model with uncased tokenization, optimized for ONNX runtime. Good for text classification, token classification, and question answering.",
                    Size = "420 MB",
                    Version = "1.0",
                    Provider = AIProvider.Local,
                    SupportedTasks = new[] { "text-classification", "token-classification", "question-answering" },
                    DownloadUrl = "https://github.com/microsoft/onnxruntime-inference-examples/raw/main/python/notebooks/assets/bert-base-uncased-11.onnx"
                },
                new ModelInfo
                {
                    Id = "onnx-distilbert-base-uncased",
                    Name = "DistilBERT Base Uncased",
                    Description = "Distilled version of BERT base, smaller and faster while retaining 95% of performance. Good for text classification and embeddings.",
                    Size = "250 MB",
                    Version = "1.0",
                    Provider = AIProvider.Local,
                    SupportedTasks = new[] { "text-classification", "embeddings" },
                    DownloadUrl = "https://github.com/microsoft/onnxruntime-inference-examples/raw/main/python/notebooks/assets/distilbert-base-uncased-11.onnx"
                },
                new ModelInfo
                {
                    Id = "onnx-ssd-10",
                    Name = "SSD Object Detection",
                    Description = "Single Shot MultiBox Detector for object detection. Detects 80 different object classes in images.",
                    Size = "78 MB",
                    Version = "1.0",
                    Provider = AIProvider.Local,
                    SupportedTasks = new[] { "object-detection" },
                    DownloadUrl = "https://github.com/onnx/models/raw/main/vision/object_detection_segmentation/ssd/model/ssd-10.onnx"
                },
                new ModelInfo
                {
                    Id = "onnx-mobilebert",
                    Name = "MobileBERT",
                    Description = "Lightweight BERT model with similar performance but much smaller size, ideal for mobile and edge devices.",
                    Size = "105 MB",
                    Version = "1.0",
                    Provider = AIProvider.Local,
                    SupportedTasks = new[] { "text-classification", "question-answering" },
                    DownloadUrl = "https://github.com/microsoft/onnxruntime-inference-examples/raw/main/python/notebooks/assets/mobilebert.onnx"
                },
                new ModelInfo
                {
                    Id = "onnx-squeezenet",
                    Name = "SqueezeNet",
                    Description = "Lightweight image classification model that's 50x smaller than AlexNet with similar accuracy.",
                    Size = "5 MB",
                    Version = "1.0",
                    Provider = AIProvider.Local,
                    SupportedTasks = new[] { "image-classification" },
                    DownloadUrl = "https://github.com/onnx/models/raw/main/vision/classification/squeezenet/model/squeezenet1.1-7.onnx"
                },
                new ModelInfo
                {
                    Id = "onnx-emotion-ferplus",
                    Name = "Emotion FERPlus",
                    Description = "Emotion recognition model that detects 8 emotions from facial expressions.",
                    Size = "34 MB",
                    Version = "1.0",
                    Provider = AIProvider.Local,
                    SupportedTasks = new[] { "emotion-recognition" },
                    DownloadUrl = "https://github.com/onnx/models/raw/main/vision/body_analysis/emotion_ferplus/model/emotion-ferplus-8.onnx"
                },
                new ModelInfo
                {
                    Id = "onnx-tiny-yolo",
                    Name = "Tiny YOLOv3",
                    Description = "Smaller version of YOLOv3 for real-time object detection, capable of detecting 80 different object classes.",
                    Size = "35 MB",
                    Version = "1.0",
                    Provider = AIProvider.Local,
                    SupportedTasks = new[] { "object-detection" },
                    DownloadUrl = "https://github.com/onnx/models/raw/main/vision/object_detection_segmentation/tiny-yolov3/model/tiny-yolov3-11.onnx"
                },
                new ModelInfo
                {
                    Id = "onnx-resnet50",
                    Name = "ResNet50",
                    Description = "Popular 50-layer deep neural network for image classification trained on ImageNet.",
                    Size = "98 MB",
                    Version = "1.0",
                    Provider = AIProvider.Local,
                    SupportedTasks = new[] { "image-classification" },
                    DownloadUrl = "https://github.com/onnx/models/raw/main/vision/classification/resnet/model/resnet50-v1-7.onnx"
                },
                new ModelInfo
                {
                    Id = "onnx-efficientnet-lite4",
                    Name = "EfficientNet-Lite4",
                    Description = "Lightweight image classification model designed for mobile and edge devices.",
                    Size = "49 MB",
                    Version = "1.0",
                    Provider = AIProvider.Local,
                    SupportedTasks = new[] { "image-classification" },
                    DownloadUrl = "https://github.com/onnx/models/raw/main/vision/classification/efficientnet-lite4/model/efficientnet-lite4-11.onnx"
                },
                new ModelInfo
                {
                    Id = "onnx-mobilenet-v2",
                    Name = "MobileNet v2",
                    Description = "Lightweight image classification model designed for mobile applications.",
                    Size = "14 MB",
                    Version = "1.0",
                    Provider = AIProvider.Local,
                    SupportedTasks = new[] { "image-classification" },
                    DownloadUrl = "https://github.com/onnx/models/raw/main/vision/classification/mobilenet/model/mobilenetv2-7.onnx"
                },
                new ModelInfo
                {
                    Id = "onnx-maskrcnn",
                    Name = "Mask R-CNN",
                    Description = "Object detection and instance segmentation model that can identify and segment multiple objects in an image.",
                    Size = "170 MB",
                    Version = "1.0",
                    Provider = AIProvider.Local,
                    SupportedTasks = new[] { "instance-segmentation", "object-detection" },
                    DownloadUrl = "https://github.com/onnx/models/raw/main/vision/object_detection_segmentation/mask-rcnn/model/MaskRCNN-10.onnx"
                },
                new ModelInfo
                {
                    Id = "onnx-faster-rcnn",
                    Name = "Faster R-CNN",
                    Description = "Fast and accurate object detection model with region proposal network.",
                    Size = "163 MB",
                    Version = "1.0",
                    Provider = AIProvider.Local,
                    SupportedTasks = new[] { "object-detection" },
                    DownloadUrl = "https://github.com/onnx/models/raw/main/vision/object_detection_segmentation/faster-rcnn/model/FasterRCNN-10.onnx"
                },
                new ModelInfo
                {
                    Id = "onnx-densenet-121",
                    Name = "DenseNet-121",
                    Description = "121-layer deep neural network for image classification with dense connections.",
                    Size = "31 MB",
                    Version = "1.0",
                    Provider = AIProvider.Local,
                    SupportedTasks = new[] { "image-classification" },
                    DownloadUrl = "https://github.com/onnx/models/raw/main/vision/classification/densenet-121/model/densenet-9.onnx"
                },
                new ModelInfo
                {
                    Id = "onnx-albert-base",
                    Name = "ALBERT Base",
                    Description = "A Lite BERT architecture that uses parameter-reduction techniques for more efficient training and inference.",
                    Size = "48 MB",
                    Version = "1.0",
                    Provider = AIProvider.Local,
                    SupportedTasks = new[] { "text-classification", "question-answering" },
                    DownloadUrl = "https://github.com/microsoft/onnxruntime-inference-examples/raw/main/python/notebooks/assets/albert-base-v2.onnx"
                },
                new ModelInfo
                {
                    Id = "onnx-mnist",
                    Name = "MNIST Handwritten Digits",
                    Description = "Simple handwritten digit classification model trained on the MNIST dataset.",
                    Size = "26 KB",
                    Version = "1.0",
                    Provider = AIProvider.Local,
                    SupportedTasks = new[] { "image-classification" },
                    DownloadUrl = "https://github.com/microsoft/onnxruntime-inference-examples/raw/main/python/notebooks/assets/mnist-8.onnx"
                },
                new ModelInfo
                {
                    Id = "onnx-unet",
                    Name = "U-Net",
                    Description = "Convolutional neural network for biomedical image segmentation.",
                    Size = "62 MB",
                    Version = "1.0",
                    Provider = AIProvider.Local,
                    SupportedTasks = new[] { "image-segmentation" },
                    DownloadUrl = "https://github.com/onnx/models/raw/main/vision/object_detection_segmentation/unet/model/unet-9.onnx"
                },
                new ModelInfo
                {
                    Id = "onnx-inception-v1",
                    Name = "Inception v1",
                    Description = "GoogLeNet Inception v1 model for image classification.",
                    Size = "28 MB",
                    Version = "1.0",
                    Provider = AIProvider.Local,
                    SupportedTasks = new[] { "image-classification" },
                    DownloadUrl = "https://github.com/onnx/models/raw/main/vision/classification/inception_and_googlenet/inception_v1/model/inception-v1-9.onnx"
                },
                new ModelInfo
                {
                    Id = "onnx-vgg16",
                    Name = "VGG-16",
                    Description = "Deep convolutional network for image classification with 16 weight layers.",
                    Size = "528 MB",
                    Version = "1.0",
                    Provider = AIProvider.Local,
                    SupportedTasks = new[] { "image-classification" },
                    DownloadUrl = "https://github.com/onnx/models/raw/main/vision/classification/vgg/model/vgg16-7.onnx"
                }
            };
        }

        // This method is deliberately disabled to avoid unauthorized access errors
        private async Task<IEnumerable<ModelInfo>> FetchOnnxModelsFromHuggingFaceAsync()
        {
            _logger.LogInformation("Hugging Face model fetching disabled to avoid authentication requirements");
            return new List<ModelInfo>();
        }
    }
}