using System;

namespace Nexi.UI.Models
{
    public class ChatMessage
    {
        public required string Content { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsUser { get; set; }
    }
}