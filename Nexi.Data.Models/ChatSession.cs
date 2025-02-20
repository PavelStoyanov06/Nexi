using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nexi.Data.Models
{
    [Table("ChatSessions")]
    public class ChatSession
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<ChatMessageData> Messages { get; set; } = new List<ChatMessageData>();

        [MaxLength(50)]
        public string ModelId { get; set; } = string.Empty;
    }
}