using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexi.Data.Context;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;

namespace Nexi.Services
{
    public class ChatStorageService : IChatStorageService
    {
        private readonly NexiDbContext _context;
        private readonly ILogger<ChatStorageService> _logger;

        public ChatStorageService(NexiDbContext context, ILogger<ChatStorageService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<ChatSession> CreateSessionAsync(string title)
        {
            var session = new ChatSession
            {
                Title = title,
                CreatedAt = DateTime.UtcNow,
                LastModifiedAt = DateTime.UtcNow
            };

            _context.ChatSessions.Add(session);
            await _context.SaveChangesAsync();
            return session;
        }

        public async Task<ChatSession?> GetSessionAsync(string id)
        {
            return await _context.ChatSessions
                .Include(s => s.Messages)
                .FirstOrDefaultAsync(s => s.Id == id);
        }

        public async Task<IEnumerable<ChatSession>> GetAllSessionsAsync()
        {
            return await _context.ChatSessions
                .Include(s => s.Messages)
                .OrderByDescending(s => s.LastModifiedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<ChatSession>> SearchSessionsAsync(string query)
        {
            query = query.ToLower();
            return await _context.ChatSessions
                .Include(s => s.Messages)
                .Where(s =>
                    s.Title.ToLower().Contains(query) ||
                    s.Messages.Any(m => m.Content.ToLower().Contains(query)))
                .OrderByDescending(s => s.LastModifiedAt)
                .ToListAsync();
        }

        public async Task SaveSessionAsync(ChatSession session)
        {
            _context.Entry(session).State = EntityState.Modified;
            await _context.SaveChangesAsync();
        }

        public async Task DeleteSessionAsync(string id)
        {
            var session = await _context.ChatSessions.FindAsync(id);
            if (session != null)
            {
                _context.ChatSessions.Remove(session);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<ChatSession> AddMessageAsync(string sessionId, ChatMessageData message)
        {
            var session = await _context.ChatSessions
                .Include(s => s.Messages)
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session == null)
                throw new KeyNotFoundException($"Session {sessionId} not found");

            message.SessionId = sessionId;
            session.Messages.Add(message);
            session.LastModifiedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return session;
        }

        public async Task<bool> ClearHistoryAsync()
        {
            try
            {
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM ChatMessages");
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM ChatSessions");
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