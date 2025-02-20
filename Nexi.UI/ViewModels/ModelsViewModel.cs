using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Linq;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Nexi.UI.ViewModels
{
    public class ModelsViewModel : ViewModelBase
    {
        private readonly IAIModelService _aiModelService;
        private readonly ILogger<ModelsViewModel> _logger;
        private ObservableCollection<AIModelData> _availableModels;
        private bool _isLoading;

        public ModelsViewModel(IAIModelService aiModelService, ILogger<ModelsViewModel> logger)
        {
            _aiModelService = aiModelService;
            _logger = logger;
            _availableModels = new ObservableCollection<AIModelData>();

            // Initialize commands
            RefreshModelsCommand = ReactiveCommand.CreateFromTask(RefreshModelsAsync);
            DownloadModelCommand = ReactiveCommand.CreateFromTask<string>(DownloadModelAsync);
            DeleteModelCommand = ReactiveCommand.CreateFromTask<string>(DeleteModelAsync);

            // Load models on startup
            _ = RefreshModelsAsync();
        }

        public ObservableCollection<AIModelData> AvailableModels
        {
            get => _availableModels;
            private set => this.RaiseAndSetIfChanged(ref _availableModels, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set => this.RaiseAndSetIfChanged(ref _isLoading, value);
        }

        public ICommand RefreshModelsCommand { get; }
        public ICommand DownloadModelCommand { get; }
        public ICommand DeleteModelCommand { get; }

        private async Task RefreshModelsAsync()
        {
            try
            {
                IsLoading = true;
                var models = await _aiModelService.GetAllModelsAsync();

                AvailableModels.Clear();
                foreach (var model in models)
                {
                    AvailableModels.Add(model);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing models");
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
                IsLoading = true;
                await _aiModelService.StartDownloadModelAsync(modelId);

                // Update the UI to show model downloading
                var model = AvailableModels.FirstOrDefault(m => m.Id == modelId);
                if (model != null)
                {
                    model.Status = ModelStatus.Downloading;
                }

                // In a real app, you would start a background download process
                // and have events to update the UI when download completes
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting model download");
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
                bool success = await _aiModelService.DeleteModelAsync(modelId);

                if (success)
                {
                    // Update the UI to show model is no longer downloaded
                    var model = AvailableModels.FirstOrDefault(m => m.Id == modelId);
                    if (model != null)
                    {
                        model.Status = ModelStatus.NotDownloaded;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting model");
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}