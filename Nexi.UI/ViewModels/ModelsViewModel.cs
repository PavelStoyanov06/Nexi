using Avalonia.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.Logging;
using Nexi.Data.Context;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Nexi.UI.ViewModels
{
    public class ModelsViewModel : ViewModelBase
    {
        private readonly IAIModelService _aiModelService;
        private readonly IModelRepository _modelRepository;
        private readonly IAIService _aiService;
        private readonly ILogger<ModelsViewModel> _logger;
        private readonly IDbContextFactory<NexiDbContext> _contextFactory;
        private ObservableCollection<ModelItemViewModel> _availableModels;
        private bool _isLoading;
        private string _statusMessage = string.Empty;
        private bool _showOnlyDownloaded = false;
        private string _searchQuery = string.Empty;
        private string _selectedCategory = "All";
        private readonly ObservableAsPropertyHelper<ObservableCollection<ModelItemViewModel>> _filteredModels;
        public ICommand ToggleAuthRequirementCommand { get; }
        private bool _isLoadingMoreModels = false;
        private bool _hasMoreModels = true;
        private int _currentPage = 0;
        private const int PAGE_SIZE = 25; // Number of models per page
        private CancellationTokenSource _loadingCts = new CancellationTokenSource();

        public ModelsViewModel(
            IAIModelService aiModelService,
            IModelRepository modelRepository,
            IAIService aiService,
            ILogger<ModelsViewModel> logger,
            IDbContextFactory<NexiDbContext> contextFactory) // Add this parameter
        {
            _aiModelService = aiModelService;
            _modelRepository = modelRepository;
            _aiService = aiService;
            _logger = logger;
            _contextFactory = contextFactory; // Add this field
            _availableModels = new ObservableCollection<ModelItemViewModel>();


            // Initialize commands
            RefreshModelsCommand = ReactiveCommand.CreateFromTask(RefreshModelsAsync);
            DownloadModelCommand = ReactiveCommand.CreateFromTask<string>(DownloadModelAsync);
            DeleteModelCommand = ReactiveCommand.CreateFromTask<string>(DeleteModelAsync);
            ToggleShowDownloadedCommand = ReactiveCommand.Create(() => ShowOnlyDownloaded = !ShowOnlyDownloaded);
            ClearSearchCommand = ReactiveCommand.Create(() => SearchQuery = string.Empty);
            ToggleAuthRequirementCommand = ReactiveCommand.CreateFromTask<string>(ToggleAuthRequirementAsync);
            LoadMoreModelsCommand = ReactiveCommand.CreateFromTask(LoadMoreModelsAsync);

            // Subscribe to AI service events
            _aiService.OnInferenceProgress += (sender, message) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    StatusMessage = message;
                });
            };

            _aiService.OnError += (sender, ex) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    StatusMessage = $"Error: {ex.Message}";
                });
            };

            // Setup filtered models based on search, category and show options
            _filteredModels = this.WhenAnyValue(
                x => x.SearchQuery,
                x => x.SelectedCategory,
                x => x.ShowOnlyDownloaded,
                x => x.AvailableModels,
                (search, category, showOnlyDownloaded, models) =>
                {
                    if (models == null) return new ObservableCollection<ModelItemViewModel>();

                    // Start with all models
                    var filtered = models.AsEnumerable();

                    // Apply search filter if present
                    if (!string.IsNullOrWhiteSpace(search))
                    {
                        var searchLower = search.Trim().ToLowerInvariant();
                        filtered = filtered.Where(m =>
                            m.Name.ToLowerInvariant().Contains(searchLower) ||
                            m.Description.ToLowerInvariant().Contains(searchLower));
                    }

                    // Apply category filter if not "All"
                    if (category != "All")
                    {
                        filtered = filtered.Where(m => GetModelCategory(m) == category);
                    }

                    // Apply downloaded filter if enabled
                    if (showOnlyDownloaded)
                    {
                        filtered = filtered.Where(m => m.Status == ModelStatus.Downloaded);
                    }

                    return new ObservableCollection<ModelItemViewModel>(filtered);
                })
                .ToProperty(this, x => x.FilteredModels);

            // Load models on startup
            _ = RefreshModelsAsync();
        }

        public bool IsLoadingMoreModels
        {
            get => _isLoadingMoreModels;
            private set => this.RaiseAndSetIfChanged(ref _isLoadingMoreModels, value);
        }

        public bool HasMoreModels
        {
            get => _hasMoreModels;
            private set => this.RaiseAndSetIfChanged(ref _hasMoreModels, value);
        }

        // Add this command
        public ICommand LoadMoreModelsCommand { get; }

        private async Task ToggleAuthRequirementAsync(string modelId)
        {
            try
            {
                // Find the model in our collection
                var model = AvailableModels.FirstOrDefault(m => m.Id == modelId);
                if (model == null) return;

                // Get the model from the database
                using var context = await _contextFactory.CreateDbContextAsync();
                var dbModel = await context.AIModels.FindAsync(modelId);

                if (dbModel == null)
                {
                    StatusMessage = $"Model {modelId} not found in database";
                    return;
                }

                // Check current auth requirement
                bool currentRequiresAuth = dbModel.Metadata.TryGetValue("RequiresAuth", out var authValue) &&
                                          authValue.Equals("true", StringComparison.OrdinalIgnoreCase);

                // Toggle the value
                dbModel.Metadata["RequiresAuth"] = (!currentRequiresAuth).ToString().ToLower();
                dbModel.LastModifiedAt = DateTime.UtcNow;

                // Save changes
                await context.SaveChangesAsync();

                // Update UI
                StatusMessage = $"Model {model.Name} authentication requirement set to: {!currentRequiresAuth}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling authentication requirement");
                StatusMessage = $"Error: {ex.Message}";
            }
        }


        public ObservableCollection<ModelItemViewModel> AvailableModels
        {
            get => _availableModels;
            private set => this.RaiseAndSetIfChanged(ref _availableModels, value);
        }

        public ObservableCollection<ModelItemViewModel> FilteredModels => _filteredModels.Value;

        public bool IsLoading
        {
            get => _isLoading;
            private set => this.RaiseAndSetIfChanged(ref _isLoading, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        public bool ShowOnlyDownloaded
        {
            get => _showOnlyDownloaded;
            set => this.RaiseAndSetIfChanged(ref _showOnlyDownloaded, value);
        }

        public string SearchQuery
        {
            get => _searchQuery;
            set => this.RaiseAndSetIfChanged(ref _searchQuery, value);
        }

        public string SelectedCategory
        {
            get => _selectedCategory;
            set => this.RaiseAndSetIfChanged(ref _selectedCategory, value);
        }

        public IEnumerable<string> Categories => new List<string>
        {
            "All",
            "Small",
            "Medium",
            "Large",
            "Specialized",
            "Multilingual"
        };

        public ICommand RefreshModelsCommand { get; }
        public ICommand DownloadModelCommand { get; }
        public ICommand DeleteModelCommand { get; }
        public ICommand ToggleShowDownloadedCommand { get; }
        public ICommand ClearSearchCommand { get; }

        private async Task RefreshModelsAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Loading models...";

                // Cancel any pending loading operations
                _loadingCts.Cancel();
                _loadingCts = new CancellationTokenSource();

                // Reset the state
                _currentPage = 0;
                HasMoreModels = true;
                AvailableModels.Clear();

                // Load the initial page
                await LoadMoreModelsAsync();
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Model loading was cancelled");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error refreshing models: {Message}", ex.Message);
                StatusMessage = $"Error loading models: {ex.Message}";
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON error parsing models: {Message}", ex.Message);
                StatusMessage = $"Error parsing model data: {ex.Message}";
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Database error saving models: {Message}", ex.Message);
                StatusMessage = $"Error saving models to database: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }


        // Add these helper methods to your ModelsViewModel.cs class

        /// <summary>
        /// Ensures the model exists in the database before attempting operations on it
        /// </summary>
        private async Task EnsureModelExistsInDatabaseAsync(ModelItemViewModel model)
        {
            try
            {
                _logger.LogInformation("Creating model {ModelId} in database", model.Id);

                // Determine if model requires authentication
                bool requiresAuth = model.DownloadUrl.Contains("huggingface.co") ||
                                    model.Category.Contains("HuggingFace");

                // Create a new AIModelData entity from the view model
                var newModel = new AIModelData
                {
                    Id = model.Id,
                    Name = model.Name,
                    Description = model.Description,
                    Size = model.Size,
                    Version = model.Version ?? "1.0",
                    Status = ModelStatus.NotDownloaded,
                    DownloadUrl = model.DownloadUrl,
                    Provider = GetProviderFromUrl(model.DownloadUrl),
                    SupportedTasks = model.SupportedTasks ?? Array.Empty<string>(),
                    CreatedAt = DateTime.UtcNow,
                    LastModifiedAt = DateTime.UtcNow,
                    // Initialize metadata with authentication requirement
                    Metadata = new Dictionary<string, string>
                    {
                        ["RequiresAuth"] = requiresAuth.ToString().ToLower(),
                        ["Source"] = requiresAuth ? "HuggingFace" : "Local"
                    }
                };

                // Get a DbContext to insert the model
                using var context = await _contextFactory.CreateDbContextAsync();
                await context.AIModels.AddAsync(newModel);
                await context.SaveChangesAsync();

                _logger.LogInformation("Successfully created model {ModelId} in database (RequiresAuth: {RequiresAuth})",
                    model.Id, requiresAuth);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error saving model {ModelId} to database", model.Id);
                throw new InvalidOperationException($"Could not save model {model.Name} to database: {ex.Message}", ex);
            }
        }


        /// <summary>
        /// Attempts to update model status, handling the case where the model doesn't exist
        /// </summary>
        private async Task TryUpdateModelStatusAsync(string modelId, ModelStatus status)
        {
            try
            {
                await _aiModelService.UpdateModelStatusAsync(modelId, status);
            }
            catch (KeyNotFoundException)
            {
                _logger.LogWarning("Could not update status for model {ModelId} - not found in database", modelId);
                // Don't rethrow since this is being called from an exception handler
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Database error updating status for model {ModelId}", modelId);
                // Don't rethrow since this is being called from an exception handler
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Invalid operation updating status for model {ModelId}", modelId);
                // Don't rethrow since this is being called from an exception handler
            }
        }

        /// <summary>
        /// Determines the AI Provider based on the download URL
        /// </summary>
        private AIProvider GetProviderFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return AIProvider.Local;

            if (url.Contains("huggingface.co"))
                return AIProvider.HuggingFace;

            if (url.Contains("openai.com"))
                return AIProvider.OpenAI;

            if (url.Contains("azure.com"))
                return AIProvider.AzureOpenAI;

            return AIProvider.Local;
        }

        private async Task LoadMoreModelsAsync()
        {
            if (IsLoadingMoreModels || !HasMoreModels)
                return;

            try
            {
                IsLoadingMoreModels = true;
                StatusMessage = $"Loading models (page {_currentPage + 1})...";

                var token = _loadingCts.Token;

                // Get available models from repository with paging
                var models = await _modelRepository.GetModelsPageAsync(_currentPage, PAGE_SIZE, token);

                // Get model data from database
                var dbModels = await _aiModelService.GetModelsBatchAsync(
                    models.Select(m => m.Id).ToList());

                // Merge the data
                var newModels = new List<ModelItemViewModel>();

                foreach (var model in models)
                {
                    token.ThrowIfCancellationRequested();

                    // Update with database information if available
                    var dbModel = dbModels.FirstOrDefault(m => m.Id == model.Id);
                    if (dbModel != null)
                    {
                        model.Status = dbModel.Status;
                        model.LocalPath = dbModel.LocalPath;
                        model.DownloadedDate = dbModel.DownloadedDate;
                    }

                    var viewModel = ModelItemViewModel.Create(model);

                    // Add category tag based on model size or specialization
                    viewModel.Category = GetModelCategory(viewModel);

                    // Add color based on model size
                    viewModel.BackgroundColor = GetModelBackgroundColor(viewModel);

                    // Add appropriate status text
                    viewModel.StatusText = GetStatusText(viewModel.Status);

                    newModels.Add(viewModel);
                }

                // Check if we have more models
                HasMoreModels = newModels.Count >= PAGE_SIZE;
                _currentPage++;

                // Add the new models to the collection on the UI thread
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var model in newModels)
                    {
                        AvailableModels.Add(model);
                    }
                });

                StatusMessage = $"Loaded {AvailableModels.Count} models" +
                               (HasMoreModels ? " (scroll for more)" : "");
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Loading more models was cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading more models: {Message}", ex.Message);
                StatusMessage = $"Error loading models: {ex.Message}";
            }
            finally
            {
                IsLoadingMoreModels = false;
            }
        }

        private async Task DownloadModelAsync(string modelId)
        {
            // Find the model in our collection
            var model = AvailableModels.FirstOrDefault(m => m.Id == modelId);
            if (model == null)
            {
                StatusMessage = $"Model with ID {modelId} not found in available models";
                return;
            }

            try
            {
                IsLoading = true;
                model.IsDownloading = true;
                model.Status = ModelStatus.Downloading;
                model.StatusText = "Downloading";
                StatusMessage = $"Starting download of {model.Name}...";

                // Get the database model, create it if it doesn't exist
                var dbModel = await _aiModelService.GetModelAsync(modelId);
                if (dbModel == null)
                {
                    // Model doesn't exist in database - we need to add it first
                    await EnsureModelExistsInDatabaseAsync(model);

                    // Fetch the model again to ensure it was created
                    dbModel = await _aiModelService.GetModelAsync(modelId);
                    if (dbModel == null)
                    {
                        throw new InvalidOperationException($"Failed to create model {modelId} in database");
                    }
                }

                // Update model status to Downloading
                await _aiModelService.UpdateModelStatusAsync(modelId, ModelStatus.Downloading);

                // Create progress reporter
                var progress = new Progress<double>(value =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        model.DownloadProgress = value;

                        // Format as percentage with proper rounding
                        int percentage = (int)Math.Round(value * 100);
                        StatusMessage = $"Downloading {model.Name}: {percentage}%";

                        // Update the status text in the model as well
                        model.StatusText = $"Downloading ({percentage}%)";
                    });
                });

                // Start the actual download process
                await _aiService.DownloadModelAsync(modelId, progress);

                // Update status when complete
                model.Status = ModelStatus.Downloaded;
                model.IsDownloading = false;
                model.StatusText = "Installed";
                StatusMessage = $"Model {model.Name} downloaded successfully.";
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogError(ex, "Model {ModelId} not found in database", modelId);
                StatusMessage = "Error: Model not found in database. Try refreshing the model list.";

                // Reset UI state
                model.Status = ModelStatus.Error;
                model.IsDownloading = false;
                model.StatusText = "Error";
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error downloading model {ModelId}: {Message}", modelId, ex.Message);
                StatusMessage = $"Download failed: Network error - {ex.Message}";

                await TryUpdateModelStatusAsync(modelId, ModelStatus.Error);

                // Reset UI state
                model.Status = ModelStatus.Error;
                model.IsDownloading = false;
                model.StatusText = "Download failed";
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "Model file not found for {ModelId}: {Message}", modelId, ex.Message);
                StatusMessage = $"Download failed: Model file not found on server";

                await TryUpdateModelStatusAsync(modelId, ModelStatus.Error);
                model.Status = ModelStatus.Error;
                model.IsDownloading = false;
                model.StatusText = "File not found";
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "IO error downloading model {ModelId}: {Message}", modelId, ex.Message);
                StatusMessage = $"Download failed: IO error - {ex.Message}";

                await TryUpdateModelStatusAsync(modelId, ModelStatus.Error);
                model.Status = ModelStatus.Error;
                model.IsDownloading = false;
                model.StatusText = "IO error";
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Authentication error downloading model {ModelId}: {Message}", modelId, ex.Message);
                StatusMessage = $"Download failed: {ex.Message}";

                await TryUpdateModelStatusAsync(modelId, ModelStatus.Error);
                model.Status = ModelStatus.Error;
                model.IsDownloading = false;
                model.StatusText = "Authentication failed";
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Invalid operation for model {ModelId}: {Message}", modelId, ex.Message);
                StatusMessage = $"Download failed: {ex.Message}";

                await TryUpdateModelStatusAsync(modelId, ModelStatus.Error);
                model.Status = ModelStatus.Error;
                model.IsDownloading = false;
                model.StatusText = "Operation error";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task DeleteModelAsync(string modelId)
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Deleting model...";

                // Find the model in our collection
                var model = AvailableModels.FirstOrDefault(m => m.Id == modelId);
                if (model == null) return;

                // First unload the model if it's loaded
                if (_aiService.IsModelLoaded(modelId))
                {
                    await _aiService.UnloadModelAsync(modelId);
                }

                // Delete model files (we'll rely on AIModelService for this)
                bool success = await _aiModelService.DeleteModelAsync(modelId);

                if (success)
                {
                    // Update UI
                    model.Status = ModelStatus.NotDownloaded;
                    model.StatusText = "Available";
                    model.DownloadProgress = 0;
                    StatusMessage = $"Model {model.Name} deleted successfully.";
                }
                else
                {
                    StatusMessage = "Error deleting model.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting model {ModelId}", modelId);
                StatusMessage = $"Delete failed: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private string GetModelCategory(ModelItemViewModel model)
        {
            // Categorize based on model size and name
            if (model.Id.Contains("tiny") || model.Id.Contains("phi") ||
                model.Size.Contains("1.1") || model.Size.Contains("2.4"))
                return "Small";

            if (model.Id.Contains("7b") || model.Id.Contains("mistral") ||
                model.Size.Contains("4.1") || model.Size.Contains("3.6"))
                return "Medium";

            if (model.Id.Contains("13b") || model.Id.Contains("mixtral") ||
                model.Size.Contains("7.3") || model.Size.Contains("7.8"))
                return "Large";

            if (model.Id.Contains("code") || model.Id.Contains("bloom"))
                return "Specialized";

            if (model.Id.Contains("bloom") || model.Id.Contains("yi"))
                return "Multilingual";

            return "Medium"; // Default category
        }

        private string GetModelBackgroundColor(ModelItemViewModel model)
        {
            // Return colors based on category for visual identification
            switch (GetModelCategory(model))
            {
                case "Small": return "#4CAF50"; // Green
                case "Medium": return "#2196F3"; // Blue
                case "Large": return "#9C27B0"; // Purple
                case "Specialized": return "#FF9800"; // Orange
                case "Multilingual": return "#E91E63"; // Pink
                default: return "#607D8B"; // Blue Grey
            }
        }

        private string GetStatusText(ModelStatus status)
        {
            return status switch
            {
                ModelStatus.NotDownloaded => "Available",
                ModelStatus.Downloading => "Downloading",
                ModelStatus.Downloaded => "Installed",
                ModelStatus.Error => "Error",
                _ => "Unknown"
            };
        }
    }
}