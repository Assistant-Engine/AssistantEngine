using AssistantEngine.UI.Services.Notifications;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssistantEngine.UI.Services.Implementation.Notifications
{

    public sealed class InMemoryEvaluationStore : IEvaluationStore
    {
        private readonly ConcurrentDictionary<Guid, ScheduledEvaluation> _map = new();

        public Task<Guid> SaveAsync(ScheduledEvaluation e, CancellationToken ct = default)
        {
            e.NextCheckUtc ??= e.DueUtc ?? DateTimeOffset.UtcNow;
            _map[e.Id] = e;
            return Task.FromResult(e.Id);
        }

        public Task<ScheduledEvaluation?> GetAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_map.TryGetValue(id, out var e) ? e : null);

        public async IAsyncEnumerable<ScheduledEvaluation> DueAsync(DateTimeOffset nowUtc, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var e in _map.Values)
            {
                if (ct.IsCancellationRequested) yield break;
                if (e.State == EvalState.Pending && e.NextCheckUtc is { } next && next <= nowUtc)
                    yield return e;
            }
            await Task.CompletedTask;
        }

        public Task UpdateAsync(ScheduledEvaluation e, CancellationToken ct = default)
        {
            _map[e.Id] = e;
            return Task.CompletedTask;
        }

        public Task<IEnumerable<ScheduledEvaluation>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IEnumerable<ScheduledEvaluation>>(_map.Values.ToArray());

        public Task<bool> CancelAsync(Guid id, CancellationToken ct = default)
        {
            if (_map.TryGetValue(id, out var e))
            {
                e.State = EvalState.Disabled;
                _map[id] = e;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
    }

}
