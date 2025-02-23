using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nexi.Data.Models
{
    [Table("AIModels")]
    public class AIModelData
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Version { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Size { get; set; } = string.Empty;

        [Required]
        public ModelStatus Status { get; set; } = ModelStatus.NotDownloaded;

        public string? LocalPath { get; set; }

        public DateTime? DownloadedDate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;

        // Navigation property for configurations
        public virtual ICollection<AIRequestOptions> RequestConfigurations { get; set; } = new List<AIRequestOptions>();
    }
}