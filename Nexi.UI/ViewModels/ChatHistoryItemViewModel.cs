using System;
using System.Linq;
using Nexi.Data.Models;

namespace Nexi.UI.ViewModels
{
    public class ChatHistoryItemViewModel : ViewModelBase
    {
        public string Id { get; }
        public string Title { get; }
        public string LastMessage { get; }
        public DateTime LastMessageTime { get; }
        public DateTime LastMessageDate { get; }
        public int MessageCount { get; }

        public ChatHistoryItemViewModel(ChatSession session)
        {
            Id = session.Id;
            Title = session.Title;
            var lastMessage = session.Messages.LastOrDefault();
            LastMessage = lastMessage?.Content ?? string.Empty;
            LastMessageTime = lastMessage?.Timestamp ?? session.LastModifiedAt;
            LastMessageDate = LastMessageTime.Date;
            MessageCount = session.Messages.Count;
        }
    }
}