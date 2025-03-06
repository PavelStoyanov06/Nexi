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
            
            // First get all sessions without loading messages
            var sessions = await context.ChatSessions
                .OrderByDescending(s => s.LastModifiedAt)
                .ToListAsync();
                
            // For each session, load only the last message to show in the history view
            foreach (var session in sessions)
            {
                // Load only the last message for each session
                var lastMessage = await context.ChatMessages
                    .Where(m => m.SessionId == session.Id)
                    .OrderByDescending(m => m.Timestamp)
                    .FirstOrDefaultAsync();
                
                if (lastMessage != null)
                {
                    session.Messages = new List<ChatMessageData> { lastMessage };
                }
                else
                {
                    session.Messages = new List<ChatMessageData>();
                }
            }
            
            return sessions;
        }

        public async Task<IEnumerable<ChatSession>> GetSessionsWithoutMessagesAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.ChatSessions
                .OrderByDescending(s => s.LastModifiedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<ChatSession>> SearchSessionsAsync(string query)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            if (string.IsNullOrWhiteSpace(query))
                return await GetAllSessionsAsync();

            return await context.ChatSessions
                .Where(s => s.Title.Contains(query))
                .OrderByDescending(s => s.LastModifiedAt)
                .ToListAsync();
        }

        public async Task SaveSessionAsync(ChatSession session)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            context.ChatSessions.Update(session);
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
                throw new KeyNotFoundException($"Chat session {sessionId} not found");

            message.SessionId = sessionId;
            message.Timestamp = DateTime.UtcNow;
            
            session.Messages.Add(message);
            session.LastModifiedAt = DateTime.UtcNow;
            
            await context.SaveChangesAsync();
            
            return session;
        }

        public async Task<bool> ClearHistoryAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            try
            {
                var sessions = await context.ChatSessions.ToListAsync();
                context.ChatSessions.RemoveRange(sessions);
                await context.SaveChangesAsync();
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