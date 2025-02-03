using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace Nexi.UI.ViewModels
{
    public class ChatHistoryViewModel : ViewModelBase
    {
        private string _searchQuery = string.Empty;
        private ObservableCollection<ChatHistoryItem> _chats;

        public ChatHistoryViewModel()
        {
            // Initialize commands
            ClearSearchCommand = ReactiveCommand.Create(ClearSearch);
            OpenChatCommand = ReactiveCommand.Create<string>(OpenChat);

            // Initialize with sample data
            _chats = new ObservableCollection<ChatHistoryItem>
            {
                new ChatHistoryItem
                {
                    Id = "1",
                    Title = "General Questions",
                    LastMessage = "I'd be happy to help you with that...",
                    LastMessageTime = DateTime.Now.AddMinutes(-30),
                    LastMessageDate = DateTime.Now.Date
                },
                new ChatHistoryItem
                {
                    Id = "2",
                    Title = "Code Review",
                    LastMessage = "Here's the analysis of your code...",
                    LastMessageTime = DateTime.Now.AddHours(-2),
                    LastMessageDate = DateTime.Now.Date
                },
                new ChatHistoryItem
                {
                    Id = "3",
                    Title = "Project Planning",
                    LastMessage = "Let's break down the tasks...",
                    LastMessageTime = DateTime.Now.AddDays(-1).AddHours(-3),
                    LastMessageDate = DateTime.Now.AddDays(-1).Date
                }
            };
        }

        public string SearchQuery
        {
            get => _searchQuery;
            set => this.RaiseAndSetIfChanged(ref _searchQuery, value);
        }

        public ObservableCollection<ChatHistoryItem> FilteredChats
        {
            get
            {
                if (string.IsNullOrWhiteSpace(SearchQuery))
                    return _chats;

                return new ObservableCollection<ChatHistoryItem>(
                    _chats.Where(c =>
                        c.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                        c.LastMessage.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
                );
            }
        }

        public ICommand ClearSearchCommand { get; }
        public ICommand OpenChatCommand { get; }

        private void ClearSearch()
        {
            SearchQuery = string.Empty;
        }

        private void OpenChat(string chatId)
        {
            // TODO: Implement chat opening logic
        }
    }

    public class ChatHistoryItem
    {
        public required string Id { get; set; }
        public required string Title { get; set; }
        public required string LastMessage { get; set; }
        public DateTime LastMessageTime { get; set; }
        public DateTime LastMessageDate { get; set; }
    }
}