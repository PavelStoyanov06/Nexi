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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Index for faster querying
            modelBuilder.Entity<ChatSession>()
                .HasIndex(s => s.CreatedAt);

            modelBuilder.Entity<ChatMessageData>()
                .HasIndex(m => m.Timestamp);
        }
    }
}