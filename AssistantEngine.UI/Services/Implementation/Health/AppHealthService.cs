using System.Collections.Concurrent;
using System.Collections.Generic;

namespace AssistantEngine.UI.Services.Implementation.Health
{
    public sealed class AppHealthService : IAppHealthService
    {
        private readonly ConcurrentDictionary<HealthDomain, HealthIssue> _issues = new();

        public AppHealthSnapshot Snapshot
            => new AppHealthSnapshot(new Dictionary<HealthDomain, HealthIssue>(_issues));

        public event EventHandler<AppHealthSnapshot>? Changed;

        public HealthIssue Get(HealthDomain domain)
            => Snapshot[domain];

        public void SetStatus(
            HealthDomain domain,
            HealthLevel level,
            string? error = null,
            string? detail = null,
            IDictionary<string, string>? meta = null)
        {
            var issue = new HealthIssue(level, error, detail, DateTime.UtcNow,
                meta is null ? null : new Dictionary<string, string>(meta));

            _issues[domain] = issue;
            Changed?.Invoke(this, Snapshot);
        }
    }
}
