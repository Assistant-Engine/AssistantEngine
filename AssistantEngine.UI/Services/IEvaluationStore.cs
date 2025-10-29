using AssistantEngine.UI.Services.Implementation.Notifications;
using System.Collections.Concurrent;

namespace AssistantEngine.UI.Services.Notifications;

public interface IEvaluationStore
{
    Task<string> SaveAsync(ScheduledEvaluation e, CancellationToken ct = default);
    Task<ScheduledEvaluation?> GetAsync(string id, CancellationToken ct = default);
    IAsyncEnumerable<ScheduledEvaluation> DueAsync(DateTimeOffset nowUtc, CancellationToken ct = default);
    Task UpdateAsync(ScheduledEvaluation e, CancellationToken ct = default);
    Task<IEnumerable<ScheduledEvaluation>> ListAsync(CancellationToken ct = default);
    Task<bool> CancelAsync(string id, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);

}
