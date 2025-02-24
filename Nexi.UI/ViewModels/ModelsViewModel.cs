using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Linq;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Avalonia.Threading;
using System.Threading;

namespace Nexi.UI.ViewModels
{
    public class ModelItemViewModel : ViewModelBase
    {
        private string _id;
        private string _name;
        private string _description;
        private string _size;
        private string _version;
        private ModelStatus _status;
        private double _downloadProgress;
        private bool _isDownloading;

        public string Id
        {
            get => _id;
            set => this.RaiseAndSetIfChanged(ref _id, value);
        }

        public string Name
        {
            get => _name;
            set => this.RaiseAndSetIfChanged(ref _name, value);
        }

        public string Description
        {
            get => _description;
            set => this.RaiseAndSetIfChanged(ref _description, value);
        }

        public string Size
        {
            get => _size;
            set => this.RaiseAndSetIfChanged(ref _size, value);
        }

        public string Version
        {
            get => _version;
            set => this.RaiseAndSetIfChanged(ref _version, value);
        }

        public ModelStatus Status
        {
            get => _status;
            set => this.RaiseAndSetIfChanged(ref _status, value);
        }

        public double DownloadProgress
        {
            get => _downloadProgress;
            set => this.RaiseAndSetIfChanged(ref _downloadProgress, value);
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            set => this.RaiseAndSetIfChanged(ref _isDownloading, value);
        }

        public ModelItemViewModel(ModelInfo modelInfo, AIModelData? aiModelData)
        {
            Id = modelInfo.Id;
            Name = modelInfo.Name;
            Description = modelInfo.Description;
            Size = modelInfo.Size;
            Version = modelInfo.Version;
            Status = aiModelData?.Status ?? ModelStatus.NotDownloaded;
            DownloadProgress = 0;
            IsDownloading = Status == ModelStatus.Downloading;
        }

        // Creates a ModelItemViewModel by combining ModelInfo and AIModelData
        public static ModelItemViewModel Create(ModelInfo modelInfo, AIModelData? aiModelData)
        {
            return new ModelItemViewModel(modelInfo, aiModelData);
        }
    }

    public class ModelsViewModel : ViewModelBase
    {
        private readonly IAIModelService _aiModelService;
        private readonly IModelRepository _modelRepository;
        private readonly IAIService _aiService;
        private readonly ILogger<ModelsViewModel> _logger;
        private ObservableCollection<ModelItemViewModel> _availableModels;
        private bool _isLoading;
        private string _statusMessage = string.Empty;

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

            // Load models on startup
            _ = RefreshModelsAsync();
        }

        public ObservableCollection<ModelItemViewModel> AvailableModels
        {
            get => _availableModels;
            private set => this.RaiseAndSetIfChanged(ref _availableModels, value);
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

        public ICommand RefreshModelsCommand { get; }
        public ICommand DownloadModelCommand { get; }
        public ICommand DeleteModelCommand { get; }

        private async Task RefreshModelsAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Loading models...";

                // Get model info from repository
                var modelInfos = await _modelRepository.GetAvailableModelsAsync();

                // Get model data from database
                var aiModels = await _aiModelService.GetAllModelsAsync();

                // Merge the data
                var viewModels = new ObservableCollection<ModelItemViewModel>();

                foreach (var modelInfo in modelInfos)
                {
                    var aiModel = aiModels.FirstOrDefault(m => m.Id == modelInfo.Id);
                    viewModels.Add(ModelItemViewModel.Create(modelInfo, aiModel));
                }

                AvailableModels = viewModels;
                StatusMessage = $"Loaded {viewModels.Count} models.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing models");
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
                StatusMessage = $"Model {model.Name} downloaded successfully.";

                // Update DB status
                await _aiModelService.UpdateModelStatusAsync(modelId, ModelStatus.Downloaded);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading model {ModelId}", modelId);
                StatusMessage = $"Download failed: {ex.Message}";

                // Update UI to show error
                var model = AvailableModels.FirstOrDefault(m => m.Id == modelId);
                if (model != null)
                {
                    model.Status = ModelStatus.Error;
                    model.IsDownloading = false;
                }

                // Update DB status
                await _aiModelService.UpdateModelStatusAsync(modelId, ModelStatus.Error);
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
    }
}