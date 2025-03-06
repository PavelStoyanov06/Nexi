using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nexi.Data.Models
{
    [Table("UserSettings")]
    public class UserSettings
    {
        [Key]
        public int Id { get; set; }

        // AI Model Settings
        public string? SelectedModelId { get; set; }

        [ForeignKey("SelectedModelId")]
        public virtual AIModelData? SelectedModel { get; set; }

        public bool UseGPU { get; set; } = false;

        // LlamaSharp Settings
        public int ContextSize { get; set; } = 1024;
        public int GpuLayerCount { get; set; } = 5;

        // Voice Settings
        [MaxLength(100)]
        public string? SelectedInputDevice { get; set; }

        public int InputSensitivity { get; set; } = 50;

        // Interface Settings
        public ThemeMode SelectedTheme { get; set; } = ThemeMode.System;

        public bool UseSystemAccent { get; set; } = true;

        [MaxLength(30)]
        public string? AccentColor { get; set; } = "#A880E4";

        // Last Updated
        public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;
    }
}