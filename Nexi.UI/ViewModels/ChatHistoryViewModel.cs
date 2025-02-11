using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Reactive.Linq;
using Microsoft.Extensions.Logging;
using Nexi.Services.Interfaces;
using Nexi.Data.Models;
using System.Reactive.Concurrency;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Nexi.UI.ViewModels
{
    public class ChatHistoryViewModel : ViewModelBase
    {
        private readonly IChatStorageService _storageService;
        private readonly ILogger<ChatHistoryViewModel> _logger;
        private readonly ICommandProcessor _commandProcessor;
        private readonly IVoiceService _voiceService;
        private readonly MainViewModel _mainViewModel;
        private string _searchQuery = string.Empty;
        private ObservableCollection<ChatHistoryItemViewModel> _chats;
        private readonly ObservableAsPropertyHelper<ObservableCollection<ChatHistoryItemViewModel>> _filteredChats;
        private bool _isLoading;

        public ChatHistoryViewModel(
            IChatStorageService storageService,
            ILogger<ChatHistoryViewModel> logger,
            ICommandProcessor commandProcessor,
            IVoiceService voiceService,
            MainViewModel mainViewModel)
        {
            _storageService = storageService;
            _logger = logger;
            _commandProcessor = commandProcessor;
            _voiceService = voiceService;
            _mainViewModel = mainViewModel;
            _chats = new ObservableCollection<ChatHistoryItemViewModel>();

            // Initialize commands
            ClearSearchCommand = ReactiveCommand.Create(ClearSearch);
            OpenChatCommand = ReactiveCommand.CreateFromTask<string>(OpenChatAsync);
            DeleteChatCommand = ReactiveCommand.CreateFromTask<string>(DeleteChatAsync);

            // Setup filtered chats
            _filteredChats = this.WhenAnyValue(x => x.SearchQuery)
                .Throttle(TimeSpan.FromMilliseconds(300))
                .Select(query => new ObservableCollection<ChatHistoryItemViewModel>(
                    _chats.Where(c =>
                        string.IsNullOrWhiteSpace(query) ||
                        c.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        c.LastMessage.Contains(query, StringComparison.OrdinalIgnoreCase))
                ))
                .ToProperty(this, x => x.FilteredChats);

            // Load initial data
            _ = LoadHistoryAsync();
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
            try
            {
                IsLoading = true;
                var sessions = await _storageService.GetAllSessionsAsync();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _chats.Clear();
                    foreach (var session in sessions.OrderByDescending(s => s.LastModifiedAt))
                    {
                        _chats.Add(new ChatHistoryItemViewModel(session));
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
            }
        }

        private async Task OpenChatAsync(string chatId)
        {
            try
            {
                var session = await _storageService.GetSessionAsync(chatId);
                if (session != null)
                {
                    var chatViewModel = new ChatViewModel(
                        _commandProcessor,
                        _voiceService,
                        _storageService,
                        chatId);

                    _mainViewModel.CurrentPage = chatViewModel;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening chat {ChatId}", chatId);
            }
        }

        private async Task DeleteChatAsync(string chatId)
        {
            try
            {
                await _storageService.DeleteSessionAsync(chatId);
                await LoadHistoryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting chat {ChatId}", chatId);
            }
        }
    }
}