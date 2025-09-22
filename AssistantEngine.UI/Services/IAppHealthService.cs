using System;
using System.Collections.Generic;

namespace AssistantEngine.UI.Services
{
    public enum HealthDomain { Ollama, VectorStore, Database, Config, Ingestion }
    public enum HealthLevel { Unknown, Healthy, Degraded, Unhealthy }

    public record HealthIssue(
        HealthLevel Level,
        string? Error,             // short error title/message (null when healthy)
        string? Detail,            // optional detail for UI/logs
        DateTime Utc,              // when this status was set
        IReadOnlyDictionary<string, string>? Meta = null // e.g., {ServerUrl, Model}
    );

    public record AppHealthSnapshot(IReadOnlyDictionary<HealthDomain, HealthIssue> Issues)
    {
        public HealthIssue this[HealthDomain d]
            => Issues.TryGetValue(d, out var i)
               ? i
               : new HealthIssue(HealthLevel.Unknown, "Not initialized", null, DateTime.UtcNow);
    }

    public interface IAppHealthService
    {
        AppHealthSnapshot Snapshot { get; }
        event EventHandler<AppHealthSnapshot>? Changed;

        // Set status for a specific domain
        void SetStatus(
            HealthDomain domain,
            HealthLevel level,
            string? error = null,
            string? detail = null,
            IDictionary<string, string>? meta = null);

        // Convenience getter
        HealthIssue Get(HealthDomain domain);
    }
}
