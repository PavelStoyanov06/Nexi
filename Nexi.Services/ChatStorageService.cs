using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;

namespace Nexi.Services
{
    public class ChatStorageService : IChatStorageService
    {
        private readonly string _storageDirectory;
        private readonly string _sessionsFile;
        private readonly ILogger<ChatStorageService> _logger;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public ChatStorageService(ILogger<ChatStorageService> logger)
        {
            _logger = logger;
            _storageDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Nexi",
                "ChatHistory"
            );
            _sessionsFile = Path.Combine(_storageDirectory, "sessions.json");

            // Ensure directory exists
            Directory.CreateDirectory(_storageDirectory);
        }

        private async Task<List<ChatSession>> LoadSessionsAsync()
        {
            try
            {
                if (!File.Exists(_sessionsFile))
                    return new List<ChatSession>();

                var json = await File.ReadAllTextAsync(_sessionsFile);
                return JsonSerializer.Deserialize<List<ChatSession>>(json) ?? new List<ChatSession>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading chat sessions");
                return new List<ChatSession>();
            }
        }

        private async Task SaveSessionsAsync(List<ChatSession> sessions)
        {
            try
            {
                var json = JsonSerializer.Serialize(sessions, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                await File.WriteAllTextAsync(_sessionsFile, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving chat sessions");
                throw;
            }
        }

        public async Task<ChatSession> CreateSessionAsync(string title)
        {
            await _lock.WaitAsync();
            try
            {
                var sessions = await LoadSessionsAsync();
                var newSession = new ChatSession
                {
                    Title = title
                };
                sessions.Add(newSession);
                await SaveSessionsAsync(sessions);
                return newSession;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<ChatSession?> GetSessionAsync(string id)
        {
            await _lock.WaitAsync();
            try
            {
                var sessions = await LoadSessionsAsync();
                return sessions.FirstOrDefault(s => s.Id == id);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<IEnumerable<ChatSession>> GetAllSessionsAsync()
        {
            await _lock.WaitAsync();
            try
            {
                return await LoadSessionsAsync();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<IEnumerable<ChatSession>> SearchSessionsAsync(string query)
        {
            await _lock.WaitAsync();
            try
            {
                var sessions = await LoadSessionsAsync();
                query = query.ToLower();
                return sessions.Where(s =>
                    s.Title.ToLower().Contains(query) ||
                    s.Messages.Any(m => m.Content.ToLower().Contains(query))
                ).ToList();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task SaveSessionAsync(ChatSession session)
        {
            await _lock.WaitAsync();
            try
            {
                var sessions = await LoadSessionsAsync();
                var existingIndex = sessions.FindIndex(s => s.Id == session.Id);
                if (existingIndex >= 0)
                    sessions[existingIndex] = session;
                else
                    sessions.Add(session);
                await SaveSessionsAsync(sessions);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task DeleteSessionAsync(string id)
        {
            await _lock.WaitAsync();
            try
            {
                var sessions = await LoadSessionsAsync();
                sessions.RemoveAll(s => s.Id == id);
                await SaveSessionsAsync(sessions);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<ChatSession> AddMessageAsync(string sessionId, ChatMessageData message)
        {
            await _lock.WaitAsync();
            try
            {
                var sessions = await LoadSessionsAsync();
                var session = sessions.FirstOrDefault(s => s.Id == sessionId);
                if (session == null)
                    throw new KeyNotFoundException($"Session {sessionId} not found");

                session.Messages.Add(message);
                session.LastModifiedAt = DateTime.UtcNow;
                await SaveSessionsAsync(sessions);
                return session;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<bool> ClearHistoryAsync()
        {
            await _lock.WaitAsync();
            try
            {
                await SaveSessionsAsync(new List<ChatSession>());
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing chat history");
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}