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

        // Model Repository URLs
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
            _httpClient.Timeout = TimeSpan.FromMinutes(2);

            _modelCachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Nexi", "ModelCache.json");

            // Ensure the directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(_modelCachePath));
        }

        public async Task<IEnumerable<AIModelData>> GetAvailableModelsAsync()
        {
            try
            {
                // Check if we have a cached response that's not expired
                if (TryGetCachedModels(out var cachedModels))
                {
                    _logger.LogInformation("Returning {Count} cached models", cachedModels.Count());

                    // Important: Ensure all cached models exist in the database
                    await EnsureModelsExistInDatabaseAsync(cachedModels);

                    return cachedModels;
                }

                // Get models from HuggingFace - this is now our primary source
                var huggingFaceModels = await FetchOnnxModelsFromHuggingFaceAsync();
                List<AIModelData> allModels;

                if (huggingFaceModels.Any())
                {
                    // We successfully got models from HuggingFace
                    _logger.LogInformation("Successfully fetched {Count} models from HuggingFace", huggingFaceModels.Count());
                    allModels = huggingFaceModels.ToList();

                    // Add a few built-in models as fallback for offline usage
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

                // Save models to database - this ensures they exist before allowing downloads
                await SaveModelsToDbAsync(allModels);

                // Cache the models
                await CacheModelsAsync(allModels);

                _logger.LogInformation("Returning {Count} models (HuggingFace + built-in)", allModels.Count);
                return allModels;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error getting available models: {Message}", ex.Message);
                return GetBuiltInOnnxModels();
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON parsing error: {Message}", ex.Message);
                return GetBuiltInOnnxModels();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Database error syncing models: {Message}", ex.Message);
                return GetBuiltInOnnxModels();
            }
        }

        private async Task EnsureModelsExistInDatabaseAsync(IEnumerable<AIModelData> models)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();

                // Get existing model IDs
                var existingIds = await context.AIModels
                    .Select(m => m.Id)
                    .ToListAsync();

                // Find models that don't exist in the database yet
                var newModels = models
                    .Where(m => !existingIds.Contains(m.Id))
                    .ToList();

                if (newModels.Any())
                {
                    _logger.LogInformation("Adding {Count} new models to database from cache", newModels.Count);
                    await context.AIModels.AddRangeAsync(newModels);
                    await context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring models exist in database");
                // We log but don't throw to avoid breaking the application
            }
        }


        public async Task<AIModelData> GetModelInfoAsync(string id)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var model = await context.AIModels.FindAsync(id);

            if (model != null)
                return model;

            // If not found in database, check built-in models
            return GetBuiltInOnnxModels().FirstOrDefault(m => m.Id == id);
        }

        public async Task RefreshModelCatalogAsync()
        {
            if (File.Exists(_modelCachePath))
            {
                File.Delete(_modelCachePath);
            }

            // Fetch fresh models
            var models = await GetAvailableModelsAsync();
            _logger.LogInformation("Refreshed ONNX model catalog with {Count} models", models.Count());
        }

        private async Task SaveModelsToDbAsync(IEnumerable<AIModelData> models)
        {
            using var context = await _contextFactory.CreateDbContextAsync();

            // Get existing models
            var existingModels = await context.AIModels.ToListAsync();
            var existingIds = existingModels.Select(m => m.Id).ToHashSet();

            // Process each model
            foreach (var model in models)
            {
                if (existingIds.Contains(model.Id))
                {
                    // Update existing model (preserving Status and LocalPath)
                    var existing = existingModels.First(m => m.Id == model.Id);

                    // Update metadata properties
                    existing.Name = model.Name;
                    existing.Description = model.Description;
                    existing.Size = model.Size;
                    existing.Version = model.Version;
                    existing.DownloadUrl = model.DownloadUrl;
                    existing.SupportedTasksJson = model.SupportedTasksJson;
                    existing.MetadataJson = model.MetadataJson;
                    existing.Provider = model.Provider;
                    existing.LastModifiedAt = DateTime.UtcNow;
                }
                else
                {
                    // Add new model
                    context.AIModels.Add(model);
                }
            }

            await context.SaveChangesAsync();
        }

        private bool TryGetCachedModels(out IEnumerable<AIModelData> models)
        {
            models = null;

            if (!File.Exists(_modelCachePath))
                return false;

            var fileInfo = new FileInfo(_modelCachePath);
            if (DateTime.UtcNow - fileInfo.LastWriteTimeUtc > _cacheExpiration)
                return false;

            string json = File.ReadAllText(_modelCachePath);
            models = JsonSerializer.Deserialize<List<AIModelData>>(json);
            return models != null && models.Any();
        }

        private async Task CacheModelsAsync(IEnumerable<AIModelData> models)
        {
            string json = JsonSerializer.Serialize(models, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(_modelCachePath, json);
            _logger.LogInformation("Cached {Count} models to {FilePath}", models.Count(), _modelCachePath);
        }

        private List<AIModelData> GetBuiltInOnnxModels()
        {
            // Essential ONNX models using reliable download URLs
            return new List<AIModelData>
            {
                new AIModelData
                {
                    Id = "onnx-bert-base-uncased",
                    Name = "BERT Base Uncased",
                    Description = "BERT base model with uncased tokenization, optimized for ONNX runtime. Good for text classification, token classification, and question answering.",
                    Size = "420 MB",
                    Version = "1.0",
                    Provider = AIProvider.Local,
                    SupportedTasks = new[] { "text-classification", "token-classification", "question-answering" },
                    DownloadUrl = "https://github.com/microsoft/onnxruntime-inference-examples/raw/main/python/notebooks/assets/bert-base-uncased-11.onnx",
                    Status = ModelStatus.NotDownloaded,
                    CreatedAt = DateTime.UtcNow,
                    LastModifiedAt = DateTime.UtcNow
                },
                new AIModelData
                {
                    Id = "onnx-distilbert-base-uncased",
                    Name = "DistilBERT Base Uncased",
                    Description = "Distilled version of BERT base, smaller and faster while retaining 95% of performance. Good for text classification and embeddings.",
                    Size = "250 MB",
                    Version = "1.0",
                    Provider = AIProvider.Local,
                    SupportedTasks = new[] { "text-classification", "embeddings" },
                    DownloadUrl = "https://github.com/microsoft/onnxruntime-inference-examples/raw/main/python/notebooks/assets/distilbert-base-uncased-11.onnx",
                    Status = ModelStatus.NotDownloaded,
                    CreatedAt = DateTime.UtcNow,
                    LastModifiedAt = DateTime.UtcNow
                },
                new AIModelData
                {
                    Id = "onnx-mobilebert",
                    Name = "MobileBERT",
                    Description = "Lightweight BERT model with similar performance but much smaller size, ideal for mobile and edge devices.",
                    Size = "105 MB",
                    Version = "1.0",
                    Provider = AIProvider.Local,
                    SupportedTasks = new[] { "text-classification", "question-answering" },
                    DownloadUrl = "https://github.com/microsoft/onnxruntime-inference-examples/raw/main/python/notebooks/assets/mobilebert.onnx",
                    Status = ModelStatus.NotDownloaded,
                    CreatedAt = DateTime.UtcNow,
                    LastModifiedAt = DateTime.UtcNow
                },
                // Add more models as needed
            };
        }

        private string GetNameFromApiElement(JsonElement element, string originalId)
        {
            // Get model name from API or format from ID
            if (element.TryGetProperty("modelId", out var modelIdElement) &&
                !string.IsNullOrEmpty(modelIdElement.GetString()))
            {
                return modelIdElement.GetString() ?? originalId;
            }
            else
            {
                // Use the last part of the ID as the name
                var parts = originalId.Split('/');
                var name = parts.Length > 1 ? parts[1] : originalId;

                // Format the name nicely - replace dashes with spaces and capitalize words
                return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
                    name.Replace('-', ' '));
            }
        }

        private string GetDescriptionFromApiElement(JsonElement element, string tag)
        {
            // Get model description or create a default one
            if (element.TryGetProperty("description", out var descElement) &&
                !string.IsNullOrEmpty(descElement.GetString()))
            {
                string description = descElement.GetString() ??
                    $"AI model for {tag} tasks from HuggingFace";

                // Truncate description if too long
                if (description.Length > 300)
                {
                    description = description.Substring(0, 297) + "...";
                }
                return description;
            }
            else if (element.TryGetProperty("pipeline_tag", out var pipelineTag) &&
                     !string.IsNullOrEmpty(pipelineTag.GetString()))
            {
                return $"AI model for {pipelineTag.GetString()} tasks";
            }
            else
            {
                return $"AI model for {tag} tasks from HuggingFace";
            }
        }

        private string[] GetTasksFromApiElement(JsonElement element, string tag)
        {
            var tasks = new List<string>();

            // Try to get tasks from tags property
            if (element.TryGetProperty("tags", out var tagsElement))
            {
                foreach (var tagElem in tagsElement.EnumerateArray())
                {
                    string tagValue = tagElem.GetString() ?? "";
                    if (!string.IsNullOrEmpty(tagValue))
                    {
                        // Clean up and format task names nicely
                        tagValue = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
                            tagValue.Replace('-', ' '));
                        tasks.Add(tagValue);
                    }
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

            // If still no tasks, use the search tag
            if (tasks.Count == 0)
            {
                var formattedTag = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
                    tag.Replace('-', ' '));
                tasks.Add(formattedTag);
            }

            return tasks.ToArray();
        }

        private string GuessSizeFromId(string modelId)
        {
            // Guess model size based on task and name
            if (modelId.Contains("small") || modelId.Contains("tiny") || modelId.Contains("mini"))
            {
                return "Small (~100MB)";
            }
            else if (modelId.Contains("base") || modelId.Contains("medium"))
            {
                return "Medium (~500MB)";
            }
            else if (modelId.Contains("large") || modelId.Contains("big"))
            {
                return "Large (~1GB+)";
            }
            else
            {
                return "Unknown";
            }
        }

        private async Task<IEnumerable<AIModelData>> FetchOnnxModelsFromHuggingFaceAsync(
            int limit = 500, // Increased from 100
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Fetching models from HuggingFace API with increased limit: {Limit}", limit);

                // Check if we have a token for authenticated requests
                var authService = _serviceProvider.GetService(typeof(IAuthenticationService)) as IAuthenticationService;
                string token = null;
                bool isAuthenticated = false;

                if (authService != null)
                {
                    token = await authService.GetTokenAsync("HuggingFace");
                    isAuthenticated = !string.IsNullOrEmpty(token);
                }

                var models = new List<AIModelData>();
                var uniqueIds = new HashSet<string>();

                // Define multiple tags to search for more models
                string[] tags = {
                    "onnx",
                    "text-generation",
                    "sentence-transformers",
                    "embedding",
                    "text-classification",
                    "text-to-image",
                    "translation",
                    "summarization",
                    "question-answering",
                    "fill-mask",
                    "feature-extraction",
                    "token-classification",
                    "table-question-answering",
                    "zero-shot-classification"
                };

                // Fetch models for each tag in parallel
                var tasks = new List<Task>();

                foreach (string tag in tags)
                {
                    for (int pageOffset = 0; pageOffset < 3; pageOffset++) // Fetch 3 pages for each tag
                    {
                        int currentOffset = offset + (pageOffset * limit);
                        string apiUrl = $"https://huggingface.co/api/models?limit={limit}&offset={currentOffset}&filter={tag}";

                        // Use Task.Run to not block on each query
                        tasks.Add(Task.Run(async () =>
                        {
                            await FetchModelsWithQuery(apiUrl, token, models, uniqueIds, tag, cancellationToken);
                        }, cancellationToken));
                    }
                }

                // Also fetch general models without a specific tag
                string generalUrl = $"https://huggingface.co/api/models?limit={limit}&offset={offset}";
                tasks.Add(Task.Run(async () =>
                {
                    await FetchModelsWithQuery(generalUrl, token, models, uniqueIds, "general", cancellationToken);
                }, cancellationToken));

                // Wait for all tasks to complete
                await Task.WhenAll(tasks);

                _logger.LogInformation("Retrieved {Count} unique models from HuggingFace", models.Count);
                return models;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Model fetching was cancelled");
                return Enumerable.Empty<AIModelData>();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error fetching models from HuggingFace API: {Message}", ex.Message);
                throw;
            }
        }


        private async Task FetchModelsWithQuery(
    string apiUrl,
    string token,
    List<AIModelData> allModels,
    HashSet<string> uniqueIds,
    string tag = "onnx",
    CancellationToken cancellationToken = default)
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

            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to fetch models from {Url}: {StatusCode}", apiUrl, response.StatusCode);
                    return;
                }

                string jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
                using var document = JsonDocument.Parse(jsonResponse);

                var newModels = new List<AIModelData>();

                foreach (var element in document.RootElement.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Get the original HuggingFace ID (e.g., "microsoft/DeepSpeed-Chat")
                    if (!element.TryGetProperty("id", out var idElement))
                        continue;

                    string originalId = idElement.GetString() ?? "";
                    string modelId = originalId.Replace("/", "-").ToLowerInvariant();

                    // Check in local list first to avoid lock contention
                    if (newModels.Any(m => m.Id == modelId))
                        continue;

                    // Create model with enhanced metadata
                    var model = new AIModelData
                    {
                        Id = modelId,
                        Name = GetNameFromApiElement(element, originalId),
                        Description = GetDescriptionFromApiElement(element, tag),
                        Provider = AIProvider.HuggingFace,
                        DownloadUrl = $"https://huggingface.co/{originalId}/resolve/main/model.onnx",
                        Version = "latest",
                        Size = GuessSizeFromId(originalId),
                        Status = ModelStatus.NotDownloaded,
                        CreatedAt = DateTime.UtcNow,
                        LastModifiedAt = DateTime.UtcNow,
                        SupportedTasks = GetTasksFromApiElement(element, tag),
                        // Add metadata for better path resolution
                        Metadata = new Dictionary<string, string>
                        {
                            ["RequiresAuth"] = "true",
                            ["Source"] = "HuggingFace",
                            ["OriginalId"] = originalId,
                            ["PotentialPath0"] = "/resolve/main/model.onnx",
                            ["PotentialPath1"] = "/resolve/main/onnx/model.onnx",
                            ["PotentialPath2"] = "/resolve/main/inference/model.onnx",
                            ["PotentialPath3"] = "/resolve/main/models/model.onnx",
                            ["SearchTag"] = tag
                        }
                    };

                    // Add download stats if available
                    if (element.TryGetProperty("downloads", out var downloads))
                    {
                        model.Metadata["Downloads"] = downloads.GetInt32().ToString();
                    }

                    newModels.Add(model);
                }

                // Now add to the shared collection with a lock for thread safety
                lock (allModels)
                {
                    foreach (var model in newModels)
                    {
                        if (!uniqueIds.Contains(model.Id))
                        {
                            uniqueIds.Add(model.Id);
                            allModels.Add(model);
                        }
                    }
                }

                _logger.LogInformation("Added {Count} models from {Url}", newModels.Count, apiUrl);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("HTTP error fetching from {Url}: {Message}", apiUrl, ex.Message);
                // Continue with other queries
            }
            catch (JsonException ex)
            {
                _logger.LogWarning("JSON parsing error from {Url}: {Message}", apiUrl, ex.Message);
                // Continue with other queries
            }
        }

        public async Task<IEnumerable<AIModelData>> GetModelsPageAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // If a fresh cache doesn't exist, create it
                if (!TryGetCachedModels(out var _))
                {
                    // Don't await - this will asynchronously start refreshing the cache
                    _ = RefreshCacheInBackgroundAsync();
                }

                // Check if we have a cached response (even if stale)
                if (TryGetCachedModels(out var allModels))
                {
                    // Calculate pagination
                    var pagedModels = allModels
                        .Skip(page * pageSize)
                        .Take(pageSize)
                        .ToList();

                    _logger.LogInformation("Returning {Count} models from page {Page}",
                        pagedModels.Count, page);

                    // Ensure all these models exist in the database
                    await EnsureModelsExistInDatabaseAsync(pagedModels);
                    return pagedModels;
                }

                // If we have no cache, fetch directly from HuggingFace with limit/offset
                var huggingFaceModels = await FetchOnnxModelsFromHuggingFaceAsync(pageSize, page * pageSize);
                if (huggingFaceModels.Any())
                {
                    await SaveModelsToDbAsync(huggingFaceModels);
                    return huggingFaceModels;
                }

                // Fall back to built-in models with paging
                return GetBuiltInOnnxModels()
                    .Skip(page * pageSize)
                    .Take(pageSize)
                    .ToList();
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Model loading was cancelled");
                return Enumerable.Empty<AIModelData>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting models page: {Message}", ex.Message);

                // Return empty list on error
                return Enumerable.Empty<AIModelData>();
            }
        }

        // Add this new method for refreshing the cache in the background
        private async Task RefreshCacheInBackgroundAsync()
        {
            try
            {
                _logger.LogInformation("Starting background cache refresh");
                await RefreshModelCatalogAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background cache refresh failed");
            }
        }


        private AIModelData CreateModelFromApiElement(JsonElement element, string originalId, string modelId, bool isAuthenticated)
        {
            // Get model name
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

            // Get model description
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
            string downloadUrl = $"https://huggingface.co/api/models/{originalId}/onnx";

            // Create model object
            var model = new AIModelData
            {
                Id = modelId,
                Name = modelName,
                Description = description,
                Provider = AIProvider.HuggingFace,
                DownloadUrl = downloadUrl,
                Version = "latest",
                Size = modelSize,
                Status = ModelStatus.NotDownloaded,
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

                model.SupportedTasks = tasks.ToArray();
            }
            else
            {
                model.SupportedTasks = new[] { "General" };
            }

            // Add metadata
            model.Metadata = new Dictionary<string, string>
            {
                ["RequiresAuth"] = "true", // Always true for HuggingFace models
                ["Source"] = "HuggingFace",
                ["OriginalId"] = originalId  // Store the original HuggingFace ID
            };

            // Store download stats if available
            if (element.TryGetProperty("downloads", out var downloads))
            {
                model.Metadata["Downloads"] = downloads.GetInt32().ToString();
            }

            return model;
        }

        public async Task<byte[]> DownloadModelWithAuthAsync(
            string modelUrl,
            string provider,
            string modelName,
            IAuthenticationService authService,
            ILogger logger)
        {
            // Get the token - this will trigger the auth dialog if needed
            var token = await authService.RequestAuthenticationAsync(provider, modelName);

            // If we get here, we have a token
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            httpClient.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("Nexi", "1.0"));
            httpClient.Timeout = TimeSpan.FromMinutes(30); // Long timeout for large downloads

            var response = await httpClient.GetAsync(modelUrl);

            // Handle specific authorization errors
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // Clear the token as it might be invalid
                await authService.ClearTokenAsync(provider);

                // Throw a more specific exception
                throw new UnauthorizedAccessException(
                    "The provided token was rejected. Please check your credentials and try again.");
            }

            // Ensure we got a success response
            response.EnsureSuccessStatusCode();

            // Return the file bytes
            return await response.Content.ReadAsByteArrayAsync();
        }
    }
}