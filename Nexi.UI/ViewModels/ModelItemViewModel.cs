using ReactiveUI;
using Nexi.Data.Models;

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
        private string _category;
        private string _backgroundColor;
        private string _statusText;
        private string[] _supportedTasks;
        private string _downloadUrl;

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

        public string Category
        {
            get => _category;
            set => this.RaiseAndSetIfChanged(ref _category, value);
        }

        public string BackgroundColor
        {
            get => _backgroundColor;
            set => this.RaiseAndSetIfChanged(ref _backgroundColor, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => this.RaiseAndSetIfChanged(ref _statusText, value);
        }

        public string[] SupportedTasks
        {
            get => _supportedTasks;
            set => this.RaiseAndSetIfChanged(ref _supportedTasks, value);
        }

        public string DownloadUrl
        {
            get => _downloadUrl;
            set => this.RaiseAndSetIfChanged(ref _downloadUrl, value);
        }

        public string TasksDisplay => SupportedTasks != null ? string.Join(", ", SupportedTasks) : string.Empty;

        public ModelItemViewModel(AIModelData model)
        {
            Id = model.Id;
            Name = model.Name;
            Description = model.Description;
            Size = model.Size;
            Version = model.Version;
            Status = model.Status;
            DownloadProgress = 0;
            IsDownloading = model.Status == ModelStatus.Downloading;
            SupportedTasks = model.SupportedTasks;
            DownloadUrl = model.DownloadUrl;
            Category = "Unknown"; // Will be set by the view model
            BackgroundColor = "#607D8B"; // Default color
            StatusText = Status.ToString();
        }

        // Creates a ModelItemViewModel from an AIModelData object
        public static ModelItemViewModel Create(AIModelData model)
        {
            return new ModelItemViewModel(model);
        }
    }
}