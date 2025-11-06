using OllamaSharp;
using OllamaSharp.Models;

namespace AssistantEngine.UI.Services
{

    public interface IModelCache : IDisposable
    {
        IReadOnlyList<Model> LocalModels { get; }
        IReadOnlyList<RunningModel> RunningModels { get; }
        DateTimeOffset LastUpdated { get; }
        bool IsRunning { get; }
        Task EnsureStartedAsync();
        Task RefreshNowAsync(CancellationToken ct = default);
        event Action? Changed;
    }

}
