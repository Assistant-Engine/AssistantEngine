using AssistantEngine.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace AssistantEngine.UI.Services.Implementation.Ingestion.Chunks
{

    public interface IChunkStoreFactory
    {
        IChunkStore Get(string name);
        void AddOrUpdate(string name, IChunkStore store); // kept for compatibility; see note below
    }

    public class ChunkStoreFactory : IChunkStoreFactory
    {
        private readonly IServiceProvider _sp;
        private readonly ConcurrentDictionary<string, Func<IServiceProvider, IChunkStore>> _builders;
        private readonly ConcurrentDictionary<string, Lazy<IChunkStore>> _cache = new(StringComparer.OrdinalIgnoreCase);

        public ChunkStoreFactory(
            IServiceProvider sp,
            ConcurrentDictionary<string, Func<IServiceProvider, IChunkStore>> builders)
        {
            _sp = sp;
            _builders = builders;
        }

        public IChunkStore Get(string name)
        {
            if (!_builders.TryGetValue(name, out var build))
                throw new KeyNotFoundException($"No chunk store registered as '{name}'");

            // Health gate: avoid touching VectorStoreCollection until embeddings are configured.
            var health = _sp.GetRequiredService<IAppHealthService>();
            var vs = health.Get(HealthDomain.VectorStore);
            if (vs.Level == HealthLevel.Unhealthy)
                throw new InvalidOperationException(vs.Error ?? "Vector store is unhealthy.");


            var lazy = _cache.GetOrAdd(name, _ => new Lazy<IChunkStore>(() => build(_sp), isThreadSafe: true));
            return lazy.Value;
        }

        // For compatibility: direct injection path still works; this also resets the lazy cache.
        public void AddOrUpdate(string name, IChunkStore store)
        {
            _builders[name] = _ => store;
            _cache.TryRemove(name, out _);
        }
    }
}
