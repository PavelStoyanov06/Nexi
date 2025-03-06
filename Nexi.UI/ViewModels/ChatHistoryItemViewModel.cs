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
            
            // Handle case where messages might not be loaded
            if (session.Messages != null && session.Messages.Any())
            {
                var lastMessage = session.Messages.OrderByDescending(m => m.Timestamp).FirstOrDefault();
                LastMessage = lastMessage?.Content ?? string.Empty;
                LastMessageTime = lastMessage?.Timestamp ?? session.LastModifiedAt;
            }
            else
            {
                LastMessage = string.Empty;
                LastMessageTime = session.LastModifiedAt;
            }
            
            LastMessageDate = LastMessageTime.Date;
            MessageCount = session.Messages?.Count ?? 0;
        }
    }
}