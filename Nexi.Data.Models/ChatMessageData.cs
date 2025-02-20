using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nexi.Data.Models
{
    [Table("ChatMessages")]
    public class ChatMessageData
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string Content { get; set; } = string.Empty;

        [Required]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [Required]
        public bool IsUser { get; set; }

        public string? Context { get; set; }

        [Required]
        public string SessionId { get; set; } = string.Empty;

        [ForeignKey("SessionId")]
        public virtual ChatSession Session { get; set; } = null!;
    }
}