using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Nexi.Data.Models
{
    [Table("AIRequestConfigurations")]
    public class AIRequestOptions
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [Range(0.0, 1.0)]
        [Column(TypeName = "decimal(3,2)")]
        public decimal Temperature { get; set; } = 0.7M;

        [Required]
        [Range(1, 32000)]
        public int MaxTokens { get; set; } = 1000;

        [MaxLength(2000)]
        public string? SystemPrompt { get; set; }

        [Required]
        public AIProvider Provider { get; set; } = AIProvider.Local;

        // Foreign key to AIModelData
        [Required]
        public string ModelId { get; set; } = string.Empty;

        [ForeignKey("ModelId")]
        public virtual AIModelData Model { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;
    }
}
