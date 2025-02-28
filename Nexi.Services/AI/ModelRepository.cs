using Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexi.Data.Context;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;
using System.Text.Json;

namespace Nexi.Services.AI
{
    public class ModelRepository : IModelRepository
    {
        private readonly IDbContextFactory<NexiDbContext> _contextFactory;
        private readonly ILogger<ModelRepository> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _modelCachePath;
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(12);
        private readonly IServiceProvider _serviceProvider;

        // Model Repository URLs - using Microsoft ONNX Model Zoo and other public sources
        private const string ONNX_MODEL_ZOO = "https://github.com/onnx/models/tree/main/";

        public ModelRepository(
            IDbContextFactory<NexiDbContext> contextFactory,
            ILogger<ModelRepository> logger,
            IServiceProvider serviceProvider)
        {
            _contextFactory = contextFactory;
            _logger = logger;
            _serviceProvider = serviceProvider;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36");
            _httpClient.Timeout = TimeSpan.FromMinutes(2); // Increased timeout for API requests

            _modelCachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Nexi", "ModelCache.json");

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
                    _logger.LogInformation("Returning {Count} cached models", cachedModels.Count());
                    return cachedModels;
                }

                // Get models from HuggingFace - this is now our primary source
                var huggingFaceModels = await FetchOnnxModelsFromHuggingFaceAsync();
                List<ModelInfo> allModels;

                if (huggingFaceModels.Any())
                {
                    // We successfully got models from HuggingFace
                    _logger.LogInformation("Successfully fetched {Count} models from HuggingFace", huggingFaceModels.Count());
                    allModels = huggingFaceModels.ToList();

                    // Add a few built-in models as fallback for offline usage, but limit to just the important ones
                    var builtInModels = GetBuiltInOnnxModels().Where(m =>
                        m.Id.Contains("bert") ||
                        m.Id.Contains("distil") ||
                        m.Id.Contains("gpt") ||
                        m.Id.EndsWith("-tiny") ||
                        m.Id.Contains("mobile")).ToList();

                    // Add built-in models that don't clash with HuggingFace IDs
                    var existingIds = allModels.Select(m => m.Id).ToHashSet();
                    foreach (var model in builtInModels)
                    {
                        if (!existingIds.Contains(model.Id))
                        {
                            // Mark as built-in
                            model.Metadata["Source"] = "BuiltIn";
                            allModels.Add(model);
                        }
                    }
                }
                else
                {
                    // Couldn't get models from HuggingFace, fall back to built-in
                    _logger.LogWarning("Failed to get models from HuggingFace, using built-in models instead");
                    allModels = GetBuiltInOnnxModels().ToList();

                    // Mark all as built-in
                    foreach (var model in allModels)
                    {
                        model.Metadata["Source"] = "BuiltIn";
                    }
                }

                // Save models to database
                await SaveModelsToDbAsync(allModels);

                // Cache the models
                await CacheModelsAsync(allModels);

                _logger.LogInformation("Returning {Count} models (HuggingFace + built-in)", allModels.Count);
                return allModels;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available models");

                // Fall back to built-in models in case of failure
                var fallbackModels = GetBuiltInOnnxModels().ToList();
                foreach (var model in fallbackModels)
                {
                    model.Metadata["Source"] = "BuiltIn";
                }
                return fallbackModels;
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

        private async Task FetchModelsWithQuery(string apiUrl, string? token, List<ModelInfo> allModels)
        {
            try
            {
                bool isAuthenticated = !string.IsNullOrEmpty(token);
                using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);

                // Add authentication if available
                if (isAuthenticated)
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                }

                // Add user agent
                request.Headers.Add("User-Agent", "Nexi-App/1.0");

                // Make the request
                using var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                string jsonResponse = await response.Content.ReadAsStringAsync();

                // Parse the JSON response
                using var document = System.Text.Json.JsonDocument.Parse(jsonResponse);
                var existingIds = allModels.Select(m => m.Id).ToHashSet();

                foreach (var element in document.RootElement.EnumerateArray())
                {
                    try
                    {
                        // Get the original HuggingFace ID (e.g., "microsoft/DeepSpeed-Chat")
                        string originalId = element.GetProperty("id").GetString() ?? "";

                        // Convert to safe ID format (e.g., "microsoft-deepspeed-chat")
                        string modelId = originalId.Replace("/", "-").ToLowerInvariant();

                        // Skip if we already have this model
                        if (existingIds.Contains(modelId))
                        {
                            continue;
                        }

                        // Get model name - try modelId property, then use the last part of the ID
                        string modelName;
                        if (element.TryGetProperty("modelId", out var modelIdElement) && !string.IsNullOrEmpty(modelIdElement.GetString()))
                        {
                            modelName = modelIdElement.GetString() ?? originalId;
                        }
                        else
                        {
                            // Use the last part of the ID as the name
                            var parts = originalId.Split('/');
                            modelName = parts.Length > 1 ? parts[1] : originalId;
                        }

                        // Format the name nicely - replace dashes with spaces and capitalize words
                        modelName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
                            modelName.Replace('-', ' '));

                        // Get model description - sometimes it's in a pipeline_tag property
                        string description;
                        if (element.TryGetProperty("description", out var descElement) &&
                            !string.IsNullOrEmpty(descElement.GetString()))
                        {
                            description = descElement.GetString() ?? "ONNX model from HuggingFace";

                            // Truncate description if too long
                            if (description.Length > 300)
                            {
                                description = description.Substring(0, 297) + "...";
                            }
                        }
                        else if (element.TryGetProperty("pipeline_tag", out var pipelineTag) &&
                                 !string.IsNullOrEmpty(pipelineTag.GetString()))
                        {
                            description = $"ONNX model for {pipelineTag.GetString()}";
                        }
                        else
                        {
                            description = "ONNX model from HuggingFace";
                        }

                        // Guess model size based on task
                        string modelSize = "Unknown";
                        if (originalId.Contains("small") || originalId.Contains("tiny") || originalId.Contains("mini"))
                        {
                            modelSize = "Small (~100MB)";
                        }
                        else if (originalId.Contains("base") || originalId.Contains("medium"))
                        {
                            modelSize = "Medium (~500MB)";
                        }
                        else if (originalId.Contains("large") || originalId.Contains("big"))
                        {
                            modelSize = "Large (~1GB+)";
                        }

                        // Notice from the repo structure that ONNX models are often in the /onnx folder
                        // We'll use the API directly instead of guessing file paths
                        string downloadUrl = $"https://huggingface.co/api/models/{originalId}/onnx";

                        // Get model info object
                        var modelInfo = new ModelInfo
                        {
                            Id = modelId,
                            Name = modelName,
                            Description = description,
                            Provider = AIProvider.HuggingFace,
                            DownloadUrl = downloadUrl,
                            Version = "latest",
                            Size = modelSize,
                            CreatedAt = DateTime.UtcNow,
                            LastModifiedAt = DateTime.UtcNow
                        };

                        // Extract tags for supported tasks
                        if (element.TryGetProperty("tags", out var tagsElement))
                        {
                            var tasks = new List<string>();
                            foreach (var tag in tagsElement.EnumerateArray())
                            {
                                string tagValue = tag.GetString() ?? "";
                                if (!string.IsNullOrEmpty(tagValue))
                                {
                                    // Clean up and format task names nicely
                                    tagValue = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
                                        tagValue.Replace('-', ' '));
                                    tasks.Add(tagValue);
                                }
                            }

                            // If no tags were found, try pipeline_tag
                            if (tasks.Count == 0 && element.TryGetProperty("pipeline_tag", out var pipeline))
                            {
                                string pipelineValue = pipeline.GetString() ?? "";
                                if (!string.IsNullOrEmpty(pipelineValue))
                                {
                                    pipelineValue = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
                                        pipelineValue.Replace('-', ' '));
                                    tasks.Add(pipelineValue);
                                }
                            }

                            // If still no tasks, use a default
                            if (tasks.Count == 0)
                            {
                                tasks.Add("General");
                            }

                            modelInfo.SupportedTasks = tasks.ToArray();
                        }
                        else
                        {
                            modelInfo.SupportedTasks = new[] { "General" };
                        }

                        // Add metadata
                        var metadata = new Dictionary<string, string>
                        {
                            ["RequiresAuth"] = isAuthenticated ? "true" : "false",
                            ["Source"] = "HuggingFace",
                            ["OriginalId"] = originalId  // Store the original HuggingFace ID
                        };

                        // Store download stats if available
                        if (element.TryGetProperty("downloads", out var downloads))
                        {
                            metadata["Downloads"] = downloads.GetInt32().ToString();
                        }

                        modelInfo.Metadata = metadata;

                        // Add to collection
                        allModels.Add(modelInfo);
                        existingIds.Add(modelId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error parsing model entry from HuggingFace API");
                        // Continue with next model
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching models with query: {Query}", apiUrl);
                // Just log the error and continue - we'll return whatever models we managed to get
            }
        }


        private async Task<IEnumerable<ModelInfo>> FetchOnnxModelsFromHuggingFaceAsync()
        {
            try
            {
                _logger.LogInformation("Fetching models from HuggingFace API");

                // Check if we have a token for authenticated requests
                var authService = _serviceProvider.GetService(typeof(IAuthenticationService)) as IAuthenticationService;
                string? token = null;
                bool isAuthenticated = false;

                if (authService != null)
                {
                    try
                    {
                        token = await authService.GetTokenAsync("HuggingFace");
                        isAuthenticated = !string.IsNullOrEmpty(token);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to get HuggingFace token, continuing with unauthenticated requests");
                    }
                }

                var models = new List<ModelInfo>();

                // Get popular models first - specifically filtering for ONNX models
                await FetchModelsWithQuery("https://huggingface.co/api/models?limit=250&sort=downloads&filter=onnx", token, models);

                // Then get recent models
                await FetchModelsWithQuery("https://huggingface.co/api/models?limit=250&sort=lastModified&filter=onnx", token, models);

                _logger.LogInformation("Retrieved {Count} ONNX models from HuggingFace", models.Count);
                return models;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching models from HuggingFace API");
                return new List<ModelInfo>();
            }
        }
    }
}