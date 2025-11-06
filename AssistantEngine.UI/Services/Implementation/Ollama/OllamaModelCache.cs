using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using OllamaSharp;
using OllamaSharp.Models;
using AssistantEngine.UI.Services.Implementation.Ollama;
using AssistantEngine.UI.Services;
using AssistantEngine.Factories;

namespace AssistantEngine.UI.Services.Ollama;


public sealed class OllamaModelCache : IModelCache
{
    private readonly IOllamaClientResolver _resolver;
    private readonly ChatClientState _state;
    private readonly IAppHealthService _health;

    private readonly object _gate = new();
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    private List<Model> _local = new();
    private List<RunningModel> _running = new();
    private string? _lastServerKey;
    private DateTimeOffset _lastUpdated;

    public OllamaModelCache(IOllamaClientResolver resolver, ChatClientState state, IAppHealthService health)
    {
        _resolver = resolver;
        _state = state;
        _health = health;
    }

    public IReadOnlyList<Model> LocalModels => _local;
    public IReadOnlyList<RunningModel> RunningModels => _running;
    public DateTimeOffset LastUpdated => _lastUpdated;
    public bool IsRunning => _loopTask is { IsCompleted: false };

    public event Action? Changed;

    public async Task EnsureStartedAsync()
    {
        if (IsRunning) return;
        _loopCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => LoopAsync(_loopCts.Token));
        // Kick an immediate first fetch so UI gets data fast
        await RefreshNowAsync();
    }

    public async Task RefreshNowAsync(CancellationToken ct = default)
    {
        await FetchAndPublishAsync(ct);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await FetchAndPublishAsync(ct);
            }
        }
        catch (OperationCanceledException) { /* normal on dispose */ }
        finally { timer.Dispose(); }
    }

    private async Task FetchAndPublishAsync(CancellationToken ct)
    {
        // Health-gate: don't hammer Ollama if unhealthy
        var ollama = _health.Get(HealthDomain.Ollama);
        if (ollama.Level != HealthLevel.Healthy) return;

        var cfg = _state.Config;
        var serverKey = string.IsNullOrWhiteSpace(cfg.ModelProviderUrl)
            ? "http://localhost:11434"
            : cfg.ModelProviderUrl.TrimEnd('/');

        // If server target changed, force new snapshot (clear stale)
        if (!string.Equals(serverKey, _lastServerKey, StringComparison.OrdinalIgnoreCase))
        {
            lock (_gate)
            {
                _local = new();
                _running = new();
                _lastServerKey = serverKey;
            }
            Changed?.Invoke();
        }

        var client = (OllamaApiClient)_resolver.For(cfg);
        List<Model> nextLocal;
        List<RunningModel> nextRunning;

        // Small per-call timeout to avoid blocking UI
        using var shortCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        shortCts.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            var local = await client.ListLocalModelsAsync(shortCts.Token);
            var running = await client.ListRunningModelsAsync(shortCts.Token);

            nextLocal = local.ToList();
            nextRunning = running.ToList();
        }
        catch (Exception)
        {
            // Swallow transient errors; don't blow up the loop
            return;
        }

        // De-dupe: only raise event if changed
        bool changed;
        lock (_gate)
        {
            changed = !Same(nextLocal, _local) || !Same(nextRunning, _running);
            if (changed)
            {
                _local = nextLocal;
                _running = nextRunning;
                _lastUpdated = DateTimeOffset.UtcNow;
            }
        }
        if (changed) Changed?.Invoke();
    }

    private static bool Same(IReadOnlyList<Model> a, IReadOnlyList<Model> b)
        => a.Count == b.Count && a.Zip(b).All(x => x.First.Name == x.Second.Name && x.First.Digest == x.Second.Digest && x.First.Size == x.Second.Size);

    private static bool Same(IReadOnlyList<RunningModel> a, IReadOnlyList<RunningModel> b)
        => a.Count == b.Count && a.Zip(b).All(x => x.First.Name == x.Second.Name && x.First.Digest == x.Second.Digest);

    public void Dispose()
    {
        try { _loopCts?.Cancel(); } catch { }
        _loopCts?.Dispose();
    }
}
