using System.Collections.Generic;
using System.Threading.Tasks;
using Nexi.Data.Models;

namespace Nexi.Services.Interfaces
{
    public interface IChatStorageService
    {
        Task<ChatSession> CreateSessionAsync(string title);
        Task<ChatSession?> GetSessionAsync(string id);
        Task<IEnumerable<ChatSession>> GetAllSessionsAsync();
        Task<IEnumerable<ChatSession>> SearchSessionsAsync(string query);
        Task SaveSessionAsync(ChatSession session);
        Task DeleteSessionAsync(string id);
        Task<ChatSession> AddMessageAsync(string sessionId, ChatMessageData message);
        Task<bool> ClearHistoryAsync();
    }
}