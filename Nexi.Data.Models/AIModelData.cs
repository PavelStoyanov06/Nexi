using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

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

        [MaxLength(1000)]
        public string? DownloadUrl { get; set; }

        public DateTime? DownloadedDate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public AIProvider Provider { get; set; } = AIProvider.Local;

        // Navigation property for configurations
        public virtual ICollection<AIRequestOptions> RequestConfigurations { get; set; } = new List<AIRequestOptions>();

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

        // Metadata will also need to be serialized
        [NotMapped]
        public Dictionary<string, string> Metadata { get; set; } = new();

        // Serialized form of Metadata
        [Column("Metadata")]
        [MaxLength(2000)]
        public string MetadataJson
        {
            get => JsonSerializer.Serialize(Metadata);
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    Metadata = new Dictionary<string, string>();
                }
                else
                {
                    try
                    {
                        Metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(value)
                            ?? new Dictionary<string, string>();
                    }
                    catch
                    {
                        Metadata = new Dictionary<string, string>();
                    }
                }
            }
        }
    }
}