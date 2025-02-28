using Avalonia.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexi.Data.Context;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
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
        private ObservableCollection<ModelItemViewModel> _availableModels = new ObservableCollection<ModelItemViewModel>();
        private ObservableCollection<ModelItemViewModel> _filteredModels = new ObservableCollection<ModelItemViewModel>();
        private bool _isLoading;
        private string _statusMessage = string.Empty;
        private bool _showOnlyDownloaded = false;
        private string _searchQuery = string.Empty;
        private string _selectedCategory = "All";
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
            IDbContextFactory<NexiDbContext> contextFactory)
        {
            _aiModelService = aiModelService;
            _modelRepository = modelRepository;
            _aiService = aiService;
            _logger = logger;
            _contextFactory = contextFactory;

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

            // Setup subscription to property changes for filtering
            this.WhenAnyValue(
                x => x.SearchQuery,
                x => x.SelectedCategory,
                x => x.ShowOnlyDownloaded,
                x => x.AvailableModels)
                .Throttle(TimeSpan.FromMilliseconds(200))
                .Subscribe(_ => ApplyFilters());

            // Load models on startup
            _ = RefreshModelsAsync();
        }

        private void ApplyFilters()
        {
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    // Start with all models
                    var filtered = AvailableModels.AsEnumerable();

                    // Apply search filter if present
                    if (!string.IsNullOrWhiteSpace(SearchQuery))
                    {
                        var searchLower = SearchQuery.Trim().ToLowerInvariant();
                        filtered = filtered.Where(m =>
                            m.Name.ToLowerInvariant().Contains(searchLower) ||
                            m.Description.ToLowerInvariant().Contains(searchLower));
                    }

                    // Apply category filter if not "All"
                    if (SelectedCategory != "All")
                    {
                        filtered = filtered.Where(m => GetModelCategory(m) == SelectedCategory);
                    }

                    // Apply downloaded filter if enabled
                    if (ShowOnlyDownloaded)
                    {
                        filtered = filtered.Where(m => m.Status == ModelStatus.Downloaded);
                    }

                    // Update filtered models collection
                    FilteredModels.Clear();
                    foreach (var model in filtered)
                    {
                        FilteredModels.Add(model);
                    }

                    _logger.LogInformation("Applied filters, showing {Count} of {Total} models",
                        FilteredModels.Count, AvailableModels.Count);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying filters");
            }
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

        public ICommand LoadMoreModelsCommand { get; }
        public ICommand ToggleAuthRequirementCommand { get; }

        public ObservableCollection<ModelItemViewModel> AvailableModels
        {
            get => _availableModels;
            private set => this.RaiseAndSetIfChanged(ref _availableModels, value);
        }

        public ObservableCollection<ModelItemViewModel> FilteredModels
        {
            get => _filteredModels;
            private set => this.RaiseAndSetIfChanged(ref _filteredModels, value);
        }

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
            set
            {
                this.RaiseAndSetIfChanged(ref _showOnlyDownloaded, value);
                // ApplyFilters will be called via WhenAnyValue subscription
            }
        }

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                this.RaiseAndSetIfChanged(ref _searchQuery, value);
                // ApplyFilters will be called via WhenAnyValue subscription
            }
        }

        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedCategory, value);
                // ApplyFilters will be called via WhenAnyValue subscription
            }
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

                // Clear collections on UI thread
                await Dispatcher.UIThread.InvokeAsync(() => {
                    AvailableModels.Clear();
                    FilteredModels.Clear();
                });

                // Load the initial page
                await LoadMoreModelsAsync();

                // Apply initial filtering
                ApplyFilters();
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Model loading was cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing models: {Message}", ex.Message);
                StatusMessage = $"Error loading models: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating status for model {ModelId}", modelId);
                // Don't rethrow since this is being called from an exception handler
            }
        }

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
                    // Re-apply filters with the new models
                    ApplyFilters();
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

                // Re-apply filters in case download status is a filter criterion
                ApplyFilters();
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading model {ModelId}: {Message}", modelId, ex.Message);
                StatusMessage = $"Download failed: {ex.Message}";

                await TryUpdateModelStatusAsync(modelId, ModelStatus.Error);

                // Reset UI state
                model.Status = ModelStatus.Error;
                model.IsDownloading = false;
                model.StatusText = "Error";
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

                    // Re-apply filters in case download status is a filter criterion
                    ApplyFilters();
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