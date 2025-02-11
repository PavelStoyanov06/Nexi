using System;
using System.Collections.Generic;

namespace Nexi.Data.Models
{
    public class ChatSession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;
        public List<ChatMessageData> Messages { get; set; } = new();
        public string ModelId { get; set; } = string.Empty;
    }

    public class ChatMessageData
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public bool IsUser { get; set; }
        public string? Context { get; set; }
    }
}