using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nexi.Data.Models
{
    [Table("ModelInfo")]
    public class ModelInfo
    {
        [Key]
        public string Id { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string DownloadUrl { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Size { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Version { get; set; } = string.Empty;

        // We need to serialize/deserialize this array
        [NotMapped]
        public string[] SupportedTasks { get; set; } = Array.Empty<string>();

        // Serialized form of SupportedTasks
        [Column("SupportedTasks")]
        [MaxLength(1000)]
        public string SupportedTasksJson
        {
            get => string.Join(",", SupportedTasks);
            set => SupportedTasks = string.IsNullOrEmpty(value)
                ? Array.Empty<string>()
                : value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        }

        [Required]
        public AIProvider Provider { get; set; }

        // Metadata will also need to be serialized
        [NotMapped]
        public Dictionary<string, string> Metadata { get; set; } = new();

        // Serialized form of Metadata
        [Column("Metadata")]
        [MaxLength(2000)]
        public string MetadataJson { get; set; } = "{}";

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;
    }
}