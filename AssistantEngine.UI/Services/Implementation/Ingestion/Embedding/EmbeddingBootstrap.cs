using System;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;
using OllamaSharp.Models;
using AssistantEngine.UI.Services.Models;

namespace AssistantEngine.UI.Services.Implementation.Ingestion.Embedding
{
    public static class EmbeddingBootstrap
    {
        public static void RegisterDefaultEmbedding(
            IServiceCollection services,
            IEnumerable<AssistantConfig> modelConfigs,
            TimeSpan? timeout = null,
             Action<string>? onResolved = null)
        {
            var cfg = modelConfigs.FirstOrDefault(x => x.Default) ?? modelConfigs.First();
            var server = new Uri(cfg.ModelProviderUrl);

            List<Model> models;
            try
            {
                using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(3));
                models = new OllamaApiClient(server).ListLocalModelsAsync(cts.Token)
                                                    .GetAwaiter().GetResult()
                                                    .ToList();
            }
            catch
            {
                throw new InvalidOperationException(
                    $"Cannot reach Ollama at {server}. Start it or update ModelProviderUrl in '{cfg.Id}'.");
            }

            var desired = cfg.EmbeddingModel.ModelId;
            var pick =
                models.FirstOrDefault(m => string.Equals(m.Name, desired, StringComparison.OrdinalIgnoreCase))?.Name
                ?? models.Select(m => m.Name).FirstOrDefault(n =>
                        n.Contains("embed", StringComparison.OrdinalIgnoreCase) ||
                        n.StartsWith("bge", StringComparison.OrdinalIgnoreCase) ||
                        n.StartsWith("gte", StringComparison.OrdinalIgnoreCase) ||
                        n.StartsWith("nomic", StringComparison.OrdinalIgnoreCase) ||
                        n.Contains("text-embedding", StringComparison.OrdinalIgnoreCase))
                ?? models.FirstOrDefault()?.Name;

            if (pick is null)
                throw new InvalidOperationException(
                    $"No models installed on {server}. Pull an embedding model (e.g. 'nomic-embed-text') or edit '{cfg.Id}'.");

            if (!string.Equals(pick, desired, StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"[Embeddings] '{desired}' not found; falling back to '{pick}'.");
            onResolved?.Invoke(pick); // ← new
            var defaultEmbed = new OllamaApiClient(server, pick);
            services.AddEmbeddingGenerator(defaultEmbed).UseLogging();
        }
    }
}
