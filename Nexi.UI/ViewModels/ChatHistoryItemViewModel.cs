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
            
            // Use the session title, but if it's "New Chat" and we have messages,
            // try to derive a better title from the first user message
            if (session.Title == "New Chat" && session.Messages != null && session.Messages.Any())
            {
                var firstUserMessage = session.Messages
                    .Where(m => m.IsUser)
                    .OrderBy(m => m.Timestamp)
                    .FirstOrDefault();
                    
                if (firstUserMessage != null)
                {
                    string userContent = firstUserMessage.Content;
                    Title = userContent.Length > 25 ? userContent.Substring(0, 22) + "..." : userContent;
                }
                else
                {
                    Title = session.Title;
                }
            }
            else
            {
                Title = session.Title;
            }
            
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