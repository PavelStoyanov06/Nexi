using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Linq;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using Avalonia.Threading;
using Avalonia.Media;

namespace Nexi.UI.ViewModels
{
    public class ModelsViewModel : ViewModelBase
    {
        private readonly IAIModelService _aiModelService;
        private readonly IUserSettingsService _userSettingsService;
        private readonly ILogger<ModelsViewModel> _logger;
        private ObservableCollection<PredefinedModelViewModel> _predefinedModels;
        private bool _isLoading;
        private string? _errorMessage;
        private string? _statusMessage;
        private string? _selectedModelId;

        public ModelsViewModel(
            IAIModelService aiModelService, 
            IUserSettingsService userSettingsService,
            ILogger<ModelsViewModel> logger)
        {
            _aiModelService = aiModelService;
            _userSettingsService = userSettingsService;
            _logger = logger;
            _predefinedModels = new ObservableCollection<PredefinedModelViewModel>();

            // Initialize commands
            RefreshModelsCommand = ReactiveCommand.CreateFromTask(RefreshModelsAsync);
            DownloadModelCommand = ReactiveCommand.CreateFromTask<PredefinedModelViewModel>(DownloadModelAsync);
            DeleteModelCommand = ReactiveCommand.CreateFromTask<PredefinedModelViewModel>(DeleteModelAsync);
            SelectModelCommand = ReactiveCommand.CreateFromTask<PredefinedModelViewModel>(SelectModelAsync);
            CancelDownloadCommand = ReactiveCommand.Create<PredefinedModelViewModel>(CancelDownload);

            // Initialize predefined models
            InitializePredefinedModels();

            // Load models on startup
            _ = RefreshModelsAsync();
        }

        private void InitializePredefinedModels()
        {
            var models = new List<PredefinedModelViewModel>
            {
                new PredefinedModelViewModel
                {
                    Id = "phi-3-mini-4k-instruct",
                    Name = "Phi-3 Mini 4K Instruct",
                    Description = "Microsoft's Phi-3 Mini 4K Instruct model optimized for efficient inference.",
                    DownloadUrl = "https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-gguf/resolve/main/Phi-3-mini-4k-instruct-fp16.gguf",
                    Size = "3.8 GB",
                    Quantization = "FP16"
                },
                new PredefinedModelViewModel
                {
                    Id = "meta-llama-3.1-8b-instruct",
                    Name = "Meta Llama 3.1 8B Instruct",
                    Description = "Meta's Llama 3.1 8B instruction-tuned model for general-purpose tasks.",
                    DownloadUrl = "https://huggingface.co/bartowski/Meta-Llama-3.1-8B-Instruct-GGUF/resolve/main/Meta-Llama-3.1-8B-Instruct-Q8_0.gguf",
                    Size = "4.8 GB",
                    Quantization = "Q8_0"
                },
                new PredefinedModelViewModel
                {
                    Id = "qwen2-7b-instruct",
                    Name = "Qwen2 7B Instruct",
                    Description = "Qwen2 7B instruction-tuned model with 8-bit quantization for efficient inference.",
                    DownloadUrl = "https://huggingface.co/Qwen/Qwen2-7B-Instruct-GGUF/resolve/main/qwen2-7b-instruct-q8_0.gguf?download=true",
                    Size = "4.1 GB",
                    Quantization = "Q8_0"
                },
                new PredefinedModelViewModel
                {
                    Id = "phi-4",
                    Name = "Phi-4",
                    Description = "Microsoft's Phi-4 model with 6-bit quantization for efficient inference.",
                    DownloadUrl = "https://huggingface.co/unsloth/phi-4-GGUF/resolve/main/phi-4-Q6_K.gguf",
                    Size = "5.2 GB",
                    Quantization = "Q6_K"
                },
                new PredefinedModelViewModel
                {
                    Id = "llama-3.2-3b-instruct",
                    Name = "Llama 3.2 3B Instruct",
                    Description = "Meta's Llama 3.2 3B instruction-tuned model for efficient inference.",
                    DownloadUrl = "https://huggingface.co/bartowski/Llama-3.2-3B-Instruct-GGUF/resolve/main/Llama-3.2-3B-Instruct-f16.gguf",
                    Size = "2.1 GB",
                    Quantization = "FP16"
                },
                new PredefinedModelViewModel
                {
                    Id = "llama-3.2-1b-instruct",
                    Name = "Llama 3.2 1B Instruct",
                    Description = "Meta's Llama 3.2 1B instruction-tuned model for efficient inference.",
                    DownloadUrl = "https://huggingface.co/bartowski/Llama-3.2-1B-Instruct-GGUF/resolve/main/Llama-3.2-1B-Instruct-f16.gguf",
                    Size = "1.1 GB",
                    Quantization = "FP16"
                }
            };

            PredefinedModels = new ObservableCollection<PredefinedModelViewModel>(models);
        }

        public ObservableCollection<PredefinedModelViewModel> PredefinedModels
        {
            get => _predefinedModels;
            private set => this.RaiseAndSetIfChanged(ref _predefinedModels, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set => this.RaiseAndSetIfChanged(ref _isLoading, value);
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            private set
            {
                this.RaiseAndSetIfChanged(ref _errorMessage, value);
                this.RaisePropertyChanged(nameof(HasError));
            }
        }

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        public string? StatusMessage
        {
            get => _statusMessage;
            private set
            {
                this.RaiseAndSetIfChanged(ref _statusMessage, value);
                this.RaisePropertyChanged(nameof(HasStatusMessage));
            }
        }

        public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

        public string? SelectedModelId
        {
            get => _selectedModelId;
            set => this.RaiseAndSetIfChanged(ref _selectedModelId, value);
        }

        public ICommand RefreshModelsCommand { get; }
        public ICommand DownloadModelCommand { get; }
        public ICommand DeleteModelCommand { get; }
        public ICommand SelectModelCommand { get; }
        public ICommand CancelDownloadCommand { get; }

        private async Task RefreshModelsAsync()
        {
            try
            {
                IsLoading = true;
                ErrorMessage = null;
                StatusMessage = null;

                // Get current settings to highlight the selected model
                var settings = await _userSettingsService.GetSettingsAsync();
                SelectedModelId = settings.SelectedModelId;

                // Update model statuses
                foreach (var model in PredefinedModels)
                {
                    var isDownloaded = await _aiModelService.IsModelDownloadedAsync(model.Id);
                    model.IsDownloaded = isDownloaded;
                    model.Status = isDownloaded ? "Downloaded" : "Not Downloaded";
                    model.StatusBrush = isDownloaded ? 
                        new SolidColorBrush(Color.Parse("#2ecc71")) : 
                        new SolidColorBrush(Color.Parse("#e74c3c"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing models");
                ErrorMessage = $"Error refreshing models: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task DownloadModelAsync(PredefinedModelViewModel model)
        {
            try
            {
                // Set model as downloading on the UI thread
                Dispatcher.UIThread.Post(() =>
                {
                    model.IsDownloading = true;
                    model.IsDownloaded = false;
                    model.Status = "Downloading...";
                    model.Progress = 0;
                    model.ProgressText = "0%";
                    model.StatusBrush = new SolidColorBrush(Color.Parse("#3498db"));
                    
                    // Force property change notification
                    model.RaisePropertyChanged(nameof(model.IsDownloading));
                    model.RaisePropertyChanged(nameof(model.CanDownload));
                });

                // Create a progress reporter
                var progress = new Progress<(string, int)>(update =>
                {
                    var (currentFile, percent) = update;
                    
                    // Update on UI thread
                    Dispatcher.UIThread.Post(() =>
                    {
                        model.Progress = percent;
                        model.Status = $"Downloading... {percent}%";
                        model.ProgressText = $"{percent}%";
                        _logger.LogDebug($"Download progress for {model.Name}: {percent}%");
                        
                        // Force property change notification
                        model.RaisePropertyChanged(nameof(model.IsDownloading));
                    });
                });

                // Start download
                _logger.LogInformation($"Starting download of {model.Name} from {model.DownloadUrl}");
                await _aiModelService.DownloadGgufModelAsync(model.Id, model.DownloadUrl, progress);

                // Update status on UI thread
                Dispatcher.UIThread.Post(() =>
                {
                    model.IsDownloading = false;
                    model.IsDownloaded = true;
                    model.Status = "Downloaded";
                    model.Progress = 100;
                    model.ProgressText = "100%";
                    model.StatusBrush = new SolidColorBrush(Color.Parse("#2ecc71"));
                    
                    // Force property change notification
                    model.RaisePropertyChanged(nameof(model.IsDownloading));
                    model.RaisePropertyChanged(nameof(model.CanDownload));
                });

                StatusMessage = $"Successfully downloaded {model.Name}";
                _logger.LogInformation($"Successfully downloaded {model.Name}");
                
                // Refresh models to update UI
                await RefreshModelsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error downloading model {model.Name}");
                ErrorMessage = $"Error downloading model: {ex.Message}";
                
                // Reset model status on UI thread
                Dispatcher.UIThread.Post(() =>
                {
                    model.IsDownloading = false;
                    model.IsDownloaded = false;
                    model.Status = "Download Failed";
                    model.StatusBrush = new SolidColorBrush(Color.Parse("#e74c3c"));
                    
                    // Force property change notification
                    model.RaisePropertyChanged(nameof(model.IsDownloading));
                    model.RaisePropertyChanged(nameof(model.CanDownload));
                });
            }
        }

        private async Task DeleteModelAsync(PredefinedModelViewModel model)
        {
            try
            {
                IsLoading = true;
                ErrorMessage = null;
                StatusMessage = null;

                // Delete the model
                var result = await _aiModelService.DeleteModelAsync(model.Id);
                if (result)
                {
                    // Update model status
                    model.IsDownloaded = false;
                    model.Status = "Not Downloaded";
                    model.StatusBrush = new SolidColorBrush(Color.Parse("#e74c3c"));
                    
                    StatusMessage = $"{model.Name} deleted successfully.";
                    _logger.LogInformation($"Model {model.Id} deleted successfully");
                }
                else
                {
                    ErrorMessage = "Failed to delete model.";
                    _logger.LogWarning($"Failed to delete model {model.Id}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting model {model.Id}");
                ErrorMessage = $"Error deleting model: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task SelectModelAsync(PredefinedModelViewModel model)
        {
            try
            {
                await _userSettingsService.UpdateSelectedModelAsync(model.Id);
                SelectedModelId = model.Id;
                StatusMessage = $"{model.Name} has been set as the default model.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error selecting model");
                ErrorMessage = $"Error selecting model: {ex.Message}";
            }
        }

        private void CancelDownload(PredefinedModelViewModel model)
        {
            try
            {
                _logger.LogInformation($"Cancelling download for model {model.Name}");
                
                if (_aiModelService.CancelDownload(model.Id))
                {
                    // Update UI
                    model.IsDownloading = false;
                    model.Status = "Download Cancelled";
                    model.StatusBrush = new SolidColorBrush(Color.Parse("#e74c3c"));
                    
                    StatusMessage = $"Download of {model.Name} has been cancelled.";
                }
                else
                {
                    ErrorMessage = $"Could not cancel download for {model.Name}. No active download found.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error cancelling download for model {model.Id}");
                ErrorMessage = $"Error cancelling download: {ex.Message}";
            }
        }

        public void NotifyModelSelected(string modelId)
        {
            SelectedModelId = modelId;
            _logger.LogInformation($"Model selection notification received for {modelId}");
            StatusMessage = $"Model has been set as the default model.";
        }
    }

    public class PredefinedModelViewModel : ViewModelBase
    {
        private bool _isDownloading;
        private bool _isDownloaded;
        private int _progress;
        private string _status = "Not Downloaded";
        private string _progressText = "0%";
        private IBrush _statusBrush = new SolidColorBrush(Color.Parse("#e74c3c"));

        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
        public string Quantization { get; set; } = string.Empty;

        public bool IsDownloading
        {
            get => _isDownloading;
            set
            {
                this.RaiseAndSetIfChanged(ref _isDownloading, value);
                this.RaisePropertyChanged(nameof(CanDownload));
            }
        }

        public bool IsDownloaded
        {
            get => _isDownloaded;
            set
            {
                this.RaiseAndSetIfChanged(ref _isDownloaded, value);
                this.RaisePropertyChanged(nameof(CanDownload));
            }
        }

        public int Progress
        {
            get => _progress;
            set => this.RaiseAndSetIfChanged(ref _progress, value);
        }

        public string Status
        {
            get => _status;
            set => this.RaiseAndSetIfChanged(ref _status, value);
        }

        public string ProgressText
        {
            get => _progressText;
            set => this.RaiseAndSetIfChanged(ref _progressText, value);
        }

        public IBrush StatusBrush
        {
            get => _statusBrush;
            set => this.RaiseAndSetIfChanged(ref _statusBrush, value);
        }

        public bool CanDownload => !IsDownloading && !IsDownloaded;
    }
}