using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Nexi.UI.ViewModels
{
    public class ModelsViewModel : ViewModelBase
    {
        private ObservableCollection<AIModel> _availableModels;

        public ModelsViewModel()
        {
            // Initialize commands
            RefreshModelsCommand = ReactiveCommand.CreateFromTask(RefreshModelsAsync);
            DownloadModelCommand = ReactiveCommand.CreateFromTask<string>(DownloadModelAsync);
            DeleteModelCommand = ReactiveCommand.CreateFromTask<string>(DeleteModelAsync);

            // Initialize with sample data
            _availableModels = new ObservableCollection<AIModel>
            {
                new AIModel
                {
                    Id = "llama-7b",
                    Name = "LLaMA 7B",
                    Description = "A foundational large language model with 7 billion parameters.",
                    Status = ModelStatus.NotDownloaded,
                    Size = "13.5 GB",
                    Version = "2.0.0",
                    CanDownload = true,
                    CanDelete = false
                },
                new AIModel
                {
                    Id = "mistral-7b",
                    Name = "Mistral 7B",
                    Description = "High-performance language model optimized for efficiency.",
                    Status = ModelStatus.Downloading,
                    Size = "13.8 GB",
                    Version = "1.0.0",
                    CanDownload = false,
                    CanDelete = false
                },
                new AIModel
                {
                    Id = "llama-13b",
                    Name = "LLaMA 13B",
                    Description = "Enhanced version of LLaMA with 13 billion parameters.",
                    Status = ModelStatus.Downloaded,
                    Size = "24.1 GB",
                    Version = "2.0.0",
                    CanDownload = false,
                    CanDelete = true
                }
            };
        }

        public ObservableCollection<AIModel> AvailableModels
        {
            get => _availableModels;
            private set => this.RaiseAndSetIfChanged(ref _availableModels, value);
        }

        public ICommand RefreshModelsCommand { get; }
        public ICommand DownloadModelCommand { get; }
        public ICommand DeleteModelCommand { get; }

        private async Task RefreshModelsAsync()
        {
            // TODO: Implement model refresh logic
            await Task.Delay(1000); // Simulate network delay
        }

        private async Task DownloadModelAsync(string modelId)
        {
            // TODO: Implement model download logic
            await Task.Delay(1000); // Simulate download
        }

        private async Task DeleteModelAsync(string modelId)
        {
            // TODO: Implement model deletion logic
            await Task.Delay(1000); // Simulate deletion
        }
    }

    public class AIModel : ViewModelBase
    {
        public required string Id { get; set; }
        public required string Name { get; set; }
        public required string Description { get; set; }
        public ModelStatus Status { get; set; }
        public required string Size { get; set; }
        public required string Version { get; set; }
        public bool CanDownload { get; set; }
        public bool CanDelete { get; set; }
    }

    public enum ModelStatus
    {
        NotDownloaded,
        Downloading,
        Downloaded,
        Error
    }
}