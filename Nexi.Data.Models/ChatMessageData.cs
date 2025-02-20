namespace Nexi.Data.Models
{
    public class ChatMessageData
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public bool IsUser { get; set; }
        public string? Context { get; set; }
    }
}
