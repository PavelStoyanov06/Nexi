using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
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
        private ObservableCollection<ModelItemViewModel> _availableModels;
        private bool _isLoading;
        private string _statusMessage = string.Empty;
        private bool _showOnlyDownloaded = false;
        private string _searchQuery = string.Empty;
        private string _selectedCategory = "All";
        private readonly ObservableAsPropertyHelper<ObservableCollection<ModelItemViewModel>> _filteredModels;

        public ModelsViewModel(
            IAIModelService aiModelService,
            IModelRepository modelRepository,
            IAIService aiService,
            ILogger<ModelsViewModel> logger)
        {
            _aiModelService = aiModelService;
            _modelRepository = modelRepository;
            _aiService = aiService;
            _logger = logger;
            _availableModels = new ObservableCollection<ModelItemViewModel>();

            // Initialize commands
            RefreshModelsCommand = ReactiveCommand.CreateFromTask(RefreshModelsAsync);
            DownloadModelCommand = ReactiveCommand.CreateFromTask<string>(DownloadModelAsync);
            DeleteModelCommand = ReactiveCommand.CreateFromTask<string>(DeleteModelAsync);
            ToggleShowDownloadedCommand = ReactiveCommand.Create(() => ShowOnlyDownloaded = !ShowOnlyDownloaded);
            ClearSearchCommand = ReactiveCommand.Create(() => SearchQuery = string.Empty);

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

                // Get available models from repository
                var models = await _modelRepository.GetAvailableModelsAsync();

                // Get model data from database
                var dbModels = await _aiModelService.GetAllModelsAsync();

                // Merge the data
                var viewModels = new ObservableCollection<ModelItemViewModel>();

                foreach (var model in models)
                {
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

                    viewModels.Add(viewModel);
                }

                AvailableModels = viewModels;
                StatusMessage = $"Loaded {viewModels.Count} models.";
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



        private async Task DownloadModelAsync(string modelId)
        {
            try
            {
                // Find the model in our collection
                var model = AvailableModels.FirstOrDefault(m => m.Id == modelId);
                if (model == null) return;

                IsLoading = true;
                model.IsDownloading = true;
                model.Status = ModelStatus.Downloading;
                model.StatusText = "Downloading";
                StatusMessage = $"Starting download of {model.Name}...";

                // Update DB status
                await _aiModelService.UpdateModelStatusAsync(modelId, ModelStatus.Downloading);

                // Create progress reporter
                var progress = new Progress<double>(value =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        model.DownloadProgress = value;
                        StatusMessage = $"Downloading {model.Name}: {value:P0}";
                    });
                });

                // Start the actual download process
                await _aiService.DownloadModelAsync(modelId, progress);

                // Update status when complete
                model.Status = ModelStatus.Downloaded;
                model.IsDownloading = false;
                model.StatusText = "Installed";
                StatusMessage = $"Model {model.Name} downloaded successfully.";

                // Update DB status
                await _aiModelService.UpdateModelStatusAsync(modelId, ModelStatus.Downloaded);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading model {ModelId}", modelId);
                StatusMessage = $"Download failed: {ex.Message}";

                // Update DB status
                await _aiModelService.UpdateModelStatusAsync(modelId, ModelStatus.Error);

                // Find the model view model and update its status
                var model = AvailableModels.FirstOrDefault(m => m.Id == modelId);
                if (model != null)
                {
                    model.Status = ModelStatus.Error;
                    model.IsDownloading = false;
                    model.StatusText = "Download failed";
                }
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