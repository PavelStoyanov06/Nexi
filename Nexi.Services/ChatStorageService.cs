using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexi.Data.Context;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;

namespace Nexi.Services
{
    public class ChatStorageService : IChatStorageService
    {
        private readonly IDbContextFactory<NexiDbContext> _contextFactory;
        private readonly ILogger<ChatStorageService> _logger;

        public ChatStorageService(IDbContextFactory<NexiDbContext> contextFactory, ILogger<ChatStorageService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public async Task<ChatSession> CreateSessionAsync(string title)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var session = new ChatSession
            {
                Title = title,
                CreatedAt = DateTime.UtcNow,
                LastModifiedAt = DateTime.UtcNow
            };

            context.ChatSessions.Add(session);
            await context.SaveChangesAsync();
            return session;
        }

        public async Task<ChatSession?> GetSessionAsync(string id)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.ChatSessions
                .Include(s => s.Messages)
                .FirstOrDefaultAsync(s => s.Id == id);
        }

        public async Task<IEnumerable<ChatSession>> GetAllSessionsAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.ChatSessions
                .Include(s => s.Messages)
                .OrderByDescending(s => s.LastModifiedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<ChatSession>> SearchSessionsAsync(string query)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            query = query.ToLower();
            return await context.ChatSessions
                .Include(s => s.Messages)
                .Where(s =>
                    s.Title.ToLower().Contains(query) ||
                    s.Messages.Any(m => m.Content.ToLower().Contains(query)))
                .OrderByDescending(s => s.LastModifiedAt)
                .ToListAsync();
        }

        public async Task SaveSessionAsync(ChatSession session)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            context.Entry(session).State = EntityState.Modified;
            await context.SaveChangesAsync();
        }

        public async Task DeleteSessionAsync(string id)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var session = await context.ChatSessions.FindAsync(id);
            if (session != null)
            {
                context.ChatSessions.Remove(session);
                await context.SaveChangesAsync();
            }
        }

        public async Task<ChatSession> AddMessageAsync(string sessionId, ChatMessageData message)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var session = await context.ChatSessions
                .Include(s => s.Messages)
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session == null)
                throw new KeyNotFoundException($"Session {sessionId} not found");

            message.SessionId = sessionId;
            session.Messages.Add(message);
            session.LastModifiedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();
            return session;
        }

        public async Task<bool> ClearHistoryAsync()
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                await context.Database.ExecuteSqlRawAsync("DELETE FROM ChatMessages");
                await context.Database.ExecuteSqlRawAsync("DELETE FROM ChatSessions");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing chat history");
                return false;
            }
        }
    }
}