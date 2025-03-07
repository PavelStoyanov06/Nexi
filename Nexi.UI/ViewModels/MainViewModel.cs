using ReactiveUI;
using System.Windows.Input;
using Nexi.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using Microsoft.Extensions.Logging;
using System.Reactive.Linq;
using Avalonia.Threading;
using System.Linq;

namespace Nexi.UI.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private bool _isSidebarExpanded = true;
        private double _sidebarWidth;
        private ViewModelBase _currentPage;
        private const double EXPANDED_WIDTH = 250;
        private const double COLLAPSED_WIDTH = 60;
        private readonly ICommandProcessor _commandProcessor;
        private readonly IVoiceService _voiceService;
        private readonly IChatStorageService _chatStorage;
        private readonly IAIModelService _aiModelService;
        private readonly IUserSettingsService _userSettingsService;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILlamaSharpService _llamaSharpService;
        private string? _selectedModelId;
        private ChatViewModel _currentChatViewModel;
        private readonly ILogger<MainViewModel> _logger;

        public MainViewModel(
            ICommandProcessor commandProcessor,
            IVoiceService voiceService,
            IChatStorageService chatStorage,
            IAIModelService aiModelService,
            IUserSettingsService userSettingsService,
            IServiceProvider serviceProvider)
        {
            _commandProcessor = commandProcessor;
            _voiceService = voiceService;
            _chatStorage = chatStorage;
            _aiModelService = aiModelService;
            _userSettingsService = userSettingsService;
            _serviceProvider = serviceProvider;
            _logger = serviceProvider.GetRequiredService<ILogger<MainViewModel>>();
            _llamaSharpService = serviceProvider.GetRequiredService<ILlamaSharpService>();

            // Initialize with ChatView
            var logger = serviceProvider.GetRequiredService<ILogger<ChatViewModel>>();
            var webSearchService = serviceProvider.GetRequiredService<IWebSearchService>();
            _currentChatViewModel = new ChatViewModel(
                _commandProcessor, 
                _voiceService, 
                _chatStorage, 
                _aiModelService, 
                _userSettingsService,
                _llamaSharpService,
                logger,
                webSearchService);
            _currentPage = _currentChatViewModel;

            UpdateSidebarWidth();

            // Initialize commands
            ToggleSidebarCommand = ReactiveCommand.Create(() =>
            {
                IsSidebarExpanded = !IsSidebarExpanded;
            });

            NewChatCommand = ReactiveCommand.Create(NavigateToNewChat);
            ChatHistoryCommand = ReactiveCommand.Create(NavigateToChatHistory);
            ModelsCommand = ReactiveCommand.Create(NavigateToModels);
            SettingsCommand = ReactiveCommand.Create(NavigateToSettings);
            SystemInfoCommand = ReactiveCommand.Create(NavigateToSystemInfo);

            // Load the selected model from settings
            _ = RefreshSelectedModelAsync();
        }

        private async System.Threading.Tasks.Task RefreshSelectedModelAsync()
        {
            try
            {
                // Get the selected model from settings
                var settings = await _userSettingsService.GetSettingsAsync();
                
                if (!string.IsNullOrEmpty(settings.SelectedModelId))
                {
                    _selectedModelId = settings.SelectedModelId;
                    
                    // Check if the model is downloaded
                    var model = await _aiModelService.GetModelAsync(_selectedModelId);
                    if (model != null && model.Status == Data.Models.ModelStatus.Downloaded)
                    {
                        // Update the current chat view model if it exists
                        if (_currentChatViewModel != null)
                        {
                            _currentChatViewModel.SelectedModelId = _selectedModelId;
                            _logger.LogInformation($"Updated current chat view model with selected model {_selectedModelId}");
                        }
                        
                        // Log success
                        _logger.LogInformation($"Successfully loaded model {_selectedModelId}");
                    }
                    else
                    {
                        _logger.LogWarning($"Selected model {_selectedModelId} is not downloaded or not found");
                    }
                }
                else
                {
                    _logger.LogInformation("No model selected in settings");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing selected model");
            }
        }

        private async System.Threading.Tasks.Task LoadSelectedModelAsync()
        {
            try
            {
                // Get the selected model from settings
                var settings = await _userSettingsService.GetSettingsAsync();
                
                if (!string.IsNullOrEmpty(settings.SelectedModelId))
                {
                    _selectedModelId = settings.SelectedModelId;
                    
                    // Check if the model is downloaded
                    var model = await _aiModelService.GetModelAsync(_selectedModelId);
                    if (model != null && model.Status == Data.Models.ModelStatus.Downloaded)
                    {
                        // Log success
                        _logger.LogInformation($"Successfully loaded model {_selectedModelId}");
                        return;
                    }
                }
                
                // If we get here, either no model is selected or the selected model is not available
                // Try to find a downloaded model to use as default
                var models = await _aiModelService.GetAllModelsAsync();
                var downloadedModel = models.FirstOrDefault(m => m.Status == Data.Models.ModelStatus.Downloaded);
                
                if (downloadedModel != null)
                {
                    _selectedModelId = downloadedModel.Id;
                    await _userSettingsService.UpdateSelectedModelAsync(_selectedModelId);
                    _logger.LogInformation($"Saved selected model {_selectedModelId} to settings");
                }
                else
                {
                    _logger.LogWarning("No downloaded models available");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading selected model");
            }
        }

        public bool IsSidebarExpanded
        {
            get => _isSidebarExpanded;
            set
            {
                this.RaiseAndSetIfChanged(ref _isSidebarExpanded, value);
                UpdateSidebarWidth();
            }
        }

        public double SidebarWidth
        {
            get => _sidebarWidth;
            private set => this.RaiseAndSetIfChanged(ref _sidebarWidth, value);
        }

        public ViewModelBase CurrentPage
        {
            get => _currentPage;
            set => this.RaiseAndSetIfChanged(ref _currentPage, value);
        }

        private void UpdateSidebarWidth()
        {
            SidebarWidth = IsSidebarExpanded ? EXPANDED_WIDTH : COLLAPSED_WIDTH;
        }

        public ICommand ToggleSidebarCommand { get; }
        public ICommand NewChatCommand { get; }
        public ICommand ChatHistoryCommand { get; }
        public ICommand ModelsCommand { get; }
        public ICommand SettingsCommand { get; }
        public ICommand SystemInfoCommand { get; }

        private async void NavigateToNewChat()
        {
            // Refresh the selected model before creating a new chat
            await RefreshSelectedModelAsync();
            
            // Create a new session in the database
            string sessionId;
            try
            {
                var session = await _chatStorage.CreateSessionAsync("New Chat");
                sessionId = session.Id;
                _logger.LogInformation($"Created new chat session with ID: {sessionId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating new chat session");
                sessionId = Guid.NewGuid().ToString();
            }
            
            // Make sure parameters are in the correct order
            _currentChatViewModel = new ChatViewModel(
                _commandProcessor,
                _voiceService,
                _chatStorage,
                _aiModelService,
                _userSettingsService,
                _llamaSharpService,
                _serviceProvider.GetRequiredService<ILogger<ChatViewModel>>(),
                _serviceProvider.GetRequiredService<IWebSearchService>(),
                sessionId,
                false // Don't create a new session in the constructor
            );
            
            // Set the selected model
            if (!string.IsNullOrEmpty(_selectedModelId))
            {
                _currentChatViewModel.SelectedModelId = _selectedModelId;
                _logger.LogInformation($"Set selected model {_selectedModelId} on new chat view model");
            }
            else
            {
                _logger.LogWarning("No selected model available for new chat");
            }
            
            // Add welcome message
            await _currentChatViewModel.AddWelcomeMessageAsync();
            
            CurrentPage = _currentChatViewModel;
        }

        private async void NavigateToChatHistory()
        {
            // Refresh the selected model before navigating
            await RefreshSelectedModelAsync();
            
            var chatHistoryVm = _serviceProvider.GetRequiredService<ChatHistoryViewModel>();
            CurrentPage = chatHistoryVm;
        }

        private async void NavigateToModels()
        {
            // Refresh the selected model before navigating
            await RefreshSelectedModelAsync();
            
            var modelsVm = _serviceProvider.GetRequiredService<ModelsViewModel>();
            
            // Subscribe to model selection changes
            modelsVm.WhenAnyValue(x => x.SelectedModelId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Subscribe(async id => 
                {
                    _selectedModelId = id;
                    _logger.LogInformation($"Model selection changed to {id} in ModelsView");
                    
                    // Update the current chat view model
                    if (_currentChatViewModel != null)
                    {
                        _currentChatViewModel.SelectedModelId = id;
                        _logger.LogInformation($"Updated current chat view model with selected model {id}");
                    }
                    
                    // Save to settings
                    await _userSettingsService.UpdateSelectedModelAsync(id);
                    _logger.LogInformation($"Saved selected model {id} to settings");
                });
                
            CurrentPage = modelsVm;
        }

        private async void NavigateToSettings()
        {
            // Refresh the selected model before navigating
            await RefreshSelectedModelAsync();
            
            var settingsVm = _serviceProvider.GetRequiredService<SettingsViewModel>();
            
            // Refresh models in the settings view
            await settingsVm.RefreshModelsAsync();
            
            // Subscribe to model selection changes
            settingsVm.WhenAnyValue(x => x.SelectedModelIndex)
                .Where(index => index >= 0 && index < settingsVm.AvailableModels.Count)
                .Subscribe(async index => 
                {
                    var selectedModel = settingsVm.AvailableModels[index];
                    _selectedModelId = selectedModel.Id;
                    _logger.LogInformation($"Model selection changed to {_selectedModelId} in SettingsView");
                    
                    // Update the current chat view model
                    if (_currentChatViewModel != null)
                    {
                        _currentChatViewModel.SelectedModelId = _selectedModelId;
                        _logger.LogInformation($"Updated current chat view model with selected model {_selectedModelId}");
                    }
                    
                    // Save to settings
                    await _userSettingsService.UpdateSelectedModelAsync(_selectedModelId);
                    _logger.LogInformation($"Saved selected model {_selectedModelId} to settings");
                });
                
            CurrentPage = settingsVm;
        }

        private void NavigateToSystemInfo()
        {
            var systemInfoService = _serviceProvider.GetRequiredService<ISystemInfoService>();
            CurrentPage = new SystemInfoViewModel(systemInfoService);
        }
        
        public override void Dispose()
        {
            try
            {
                _logger.LogInformation("Disposing MainViewModel");
                
                // Dispose current page if it's disposable
                if (CurrentPage != null)
                {
                    _logger.LogInformation("Disposing current page: {PageType}", CurrentPage.GetType().Name);
                    CurrentPage.Dispose();
                }
                
                // Dispose current chat view model
                if (_currentChatViewModel != null)
                {
                    _logger.LogInformation("Disposing current chat view model");
                    _currentChatViewModel.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing MainViewModel");
            }
            finally
            {
                base.Dispose();
            }
        }
    }
}