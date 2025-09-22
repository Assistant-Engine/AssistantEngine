using AssistantEngine.UI.Services.Implementation.Models.Chat;
using AssistantEngine.UI.Services.Models.Chat;

namespace AssistantEngine.UI.Services
{
    public interface IChatRepository
    {    /// <summary>
         /// A map of session‐IDs → titles, populated on InitializeAsync().
         /// </summary>
         /// 
         event Action SessionsChanged;
        IReadOnlyDictionary<string, string> ChatSessionNames { get; }

        /// <summary>
        /// Load all sessions once and fill ChatSessionNames.
        /// </summary>
        Task InitializeAsync(CancellationToken ct = default);
        Task SaveAsync(ChatSession session, CancellationToken ct = default);
        Task<ChatSession?> LoadAsync(string sessionId, CancellationToken ct = default);

        Task<IEnumerable<ChatSession>> ListAllAsync(CancellationToken ct = default);

        Task ClearSessionAsync(string sessionId, CancellationToken ct = default);

    }
}
