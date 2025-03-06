using Microsoft.EntityFrameworkCore;
using Nexi.Data.Models;

namespace Nexi.Data.Context
{
    public class NexiDbContext : DbContext
    {
        public NexiDbContext(DbContextOptions<NexiDbContext> options) : base(options)
        {
        }

        public DbSet<ChatSession> ChatSessions { get; set; }
        public DbSet<ChatMessageData> ChatMessages { get; set; }
        public DbSet<AIModelData> AIModels { get; set; }
        public DbSet<UserSettings> UserSettings { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Indexes for faster querying
            modelBuilder.Entity<ChatSession>()
                .HasIndex(s => s.CreatedAt);

            modelBuilder.Entity<ChatMessageData>()
                .HasIndex(m => m.Timestamp);

            modelBuilder.Entity<AIModelData>()
                .HasIndex(m => m.Status);

            // Use a fixed date for seeding
            var seedDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            // Seed default settings
            modelBuilder.Entity<UserSettings>().HasData(
                new UserSettings
                {
                    Id = 1,
                    UseGPU = false,
                    InputSensitivity = 50,
                    SelectedTheme = ThemeMode.System,
                    UseSystemAccent = true,
                    AccentColor = "#A880E4",
                    LastModifiedAt = seedDate
                }
            );

            // Seed some default AI models
            modelBuilder.Entity<AIModelData>().HasData(
                new AIModelData
                {
                    Id = "llama-3.2-1b-instruct",
                    Name = "Llama 3.2 1B Instruct",
                    Description = "Llama 3.2 1B Instruct ONNX model for efficient inference",
                    Status = ModelStatus.NotDownloaded,
                    Size = "1.2 GB",
                    Version = "1.0.0",
                    RepositoryId = "onnx-community/Llama-3.2-1B-Instruct-ONNX",
                    CreatedAt = seedDate,
                    LastModifiedAt = seedDate
                }
            );
        }
    }
}