using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Nexi.Services.Interfaces;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Collections.Generic;
using System.Threading;

namespace Nexi.UI.ViewModels
{
    public class ChatHistoryViewModel : ViewModelBase
    {
        private readonly IChatStorageService _storageService;
        private readonly ILogger<ChatHistoryViewModel> _logger;
        private readonly ICommandProcessor _commandProcessor;
        private readonly IVoiceService _voiceService;
        private readonly MainViewModel _mainViewModel;
        private readonly ILogger<ChatViewModel> _chatViewModelLogger;
        private string _searchQuery = string.Empty;
        private ObservableCollection<ChatHistoryItemViewModel> _chats;
        private readonly ObservableAsPropertyHelper<ObservableCollection<ChatHistoryItemViewModel>> _filteredChats;
        private bool _isLoading;
        
        // Add a semaphore to prevent concurrent operations
        private readonly SemaphoreSlim _operationSemaphore = new SemaphoreSlim(1, 1);
        private bool _isRefreshing = false;

        public ChatHistoryViewModel(
            IChatStorageService storageService,
            ILogger<ChatHistoryViewModel> logger,
            ICommandProcessor commandProcessor,
            IVoiceService voiceService,
            MainViewModel mainViewModel,
            ILogger<ChatViewModel> chatViewModelLogger)
        {
            _storageService = storageService;
            _logger = logger;
            _commandProcessor = commandProcessor;
            _voiceService = voiceService;
            _mainViewModel = mainViewModel;
            _chatViewModelLogger = chatViewModelLogger;
            _chats = new ObservableCollection<ChatHistoryItemViewModel>();

            // Initialize commands
            ClearSearchCommand = ReactiveCommand.Create(ClearSearch);
            OpenChatCommand = ReactiveCommand.CreateFromTask<string>(OpenChatAsync);
            DeleteChatCommand = ReactiveCommand.CreateFromTask<string>(DeleteChatAsync);

            // Setup filtered chats with better handling of collection changes
            _filteredChats = this.WhenAnyValue(x => x.SearchQuery)
                .Throttle(TimeSpan.FromMilliseconds(300))
                .Select(query => FilterChats(query))
                .ToProperty(this, x => x.FilteredChats);

            // Load initial data
            _ = LoadHistoryAsync();
        }

        private ObservableCollection<ChatHistoryItemViewModel> FilterChats(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new ObservableCollection<ChatHistoryItemViewModel>(_chats);
                
            return new ObservableCollection<ChatHistoryItemViewModel>(
                _chats.Where(c =>
                    c.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    c.LastMessage.Contains(query, StringComparison.OrdinalIgnoreCase))
            );
        }

        public string SearchQuery
        {
            get => _searchQuery;
            set => this.RaiseAndSetIfChanged(ref _searchQuery, value);
        }

        public ObservableCollection<ChatHistoryItemViewModel> FilteredChats => _filteredChats.Value;

        public bool IsLoading
        {
            get => _isLoading;
            private set => this.RaiseAndSetIfChanged(ref _isLoading, value);
        }

        public ICommand ClearSearchCommand { get; }
        public ICommand OpenChatCommand { get; }
        public ICommand DeleteChatCommand { get; }

        private void ClearSearch()
        {
            SearchQuery = string.Empty;
        }

        private async Task LoadHistoryAsync()
        {
            // Prevent concurrent refreshes
            if (_isRefreshing)
                return;
                
            try
            {
                await _operationSemaphore.WaitAsync();
                _isRefreshing = true;
                IsLoading = true;
                
                // Set a reasonable limit for initial load
                const int pageSize = 20;
                
                // Get only the most recent sessions with limited data
                var sessions = await _storageService.GetSessionsWithoutMessagesAsync();
                var orderedSessions = sessions
                    .OrderByDescending(s => s.LastModifiedAt)
                    .Take(pageSize)
                    .ToList();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        // Create a new collection to avoid modification issues
                        var newChats = new List<ChatHistoryItemViewModel>(orderedSessions.Count);
                        
                        foreach (var session in orderedSessions)
                        {
                            newChats.Add(new ChatHistoryItemViewModel(session));
                        }
                        
                        // Clear and repopulate in one batch to minimize UI updates
                        _chats.Clear();
                        foreach (var chat in newChats)
                        {
                            _chats.Add(chat);
                        }
                        
                        // Force property change notification
                        this.RaisePropertyChanged(nameof(FilteredChats));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error updating chat history UI");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading chat history");
            }
            finally
            {
                IsLoading = false;
                _isRefreshing = false;
                _operationSemaphore.Release();
            }
        }

        private async Task OpenChatAsync(string chatId)
        {
            try
            {
                await _operationSemaphore.WaitAsync();
                
                var session = await _storageService.GetSessionAsync(chatId);
                if (session != null)
                {
                    // Get the AIModelService from the service provider
                    var aiModelService = ((App)App.Current).Services.GetRequiredService<IAIModelService>();
                    var userSettingsService = ((App)App.Current).Services.GetRequiredService<IUserSettingsService>();
                    var llamaSharpService = ((App)App.Current).Services.GetRequiredService<ILlamaSharpService>();
                    var webSearchService = ((App)App.Current).Services.GetRequiredService<IWebSearchService>();
                    
                    var chatViewModel = new ChatViewModel(
                        _commandProcessor,
                        _voiceService,
                        _storageService,
                        aiModelService,
                        userSettingsService,
                        llamaSharpService,
                        _chatViewModelLogger,
                        webSearchService,
                        chatId,
                        false); // Don't create a new session in the constructor

                    _mainViewModel.CurrentPage = chatViewModel;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening chat {ChatId}", chatId);
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        private async Task DeleteChatAsync(string chatId)
        {
            try
            {
                await _operationSemaphore.WaitAsync();
                
                // Delete the chat from storage
                await _storageService.DeleteSessionAsync(chatId);
                
                // Update UI on the UI thread
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        // Find and remove the chat from the collection
                        var chatToRemove = _chats.FirstOrDefault(c => c.Id == chatId);
                        if (chatToRemove != null)
                        {
                            _chats.Remove(chatToRemove);
                            
                            // Force property change notification
                            this.RaisePropertyChanged(nameof(FilteredChats));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error updating UI after chat deletion");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting chat {ChatId}", chatId);
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }
        
        public override void Dispose()
        {
            _operationSemaphore.Dispose();
            base.Dispose();
        }
    }
}