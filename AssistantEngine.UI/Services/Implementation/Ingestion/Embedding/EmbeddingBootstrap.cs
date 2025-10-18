using AssistantEngine.UI.Services.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;
using OllamaSharp.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;

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

            // Platform-specific HTTP client
#if MACCATALYST || IOS
            var http = new HttpClient(new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All
            })
            {
                BaseAddress = server,
                Timeout = timeout ?? TimeSpan.FromSeconds(60)
            };
#else
            var http = new HttpClient
            {
                BaseAddress = server,
                Timeout = timeout ?? TimeSpan.FromSeconds(60)
            };
#endif

            List<Model> models;
            try
            {
                using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(60));
                models = Task.Run(async () =>
                {
                    var api = new OllamaApiClient(http);
                    var list = await api.ListLocalModelsAsync(cts.Token).ConfigureAwait(false);
                    return list.ToList();
                }).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                string probe;
                try
                {
                    var resp = http.GetAsync("/api/tags").Result;
                    probe = $"PROBE {(int)resp.StatusCode} {resp.ReasonPhrase}";
                }
                catch (Exception pex)
                {
                    probe = $"PROBE FAIL {pex.GetType().Name}: {pex.Message}";
                }

                throw new InvalidOperationException(
                    $"Cannot reach Ollama at {server}. Start it or update ModelProviderUrl in '{cfg.Id}'. ");
                    //$"Inner: {ex.GetType().Name}: {ex.Message}", ex); 
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

            onResolved?.Invoke(pick);

            var defaultEmbed = new OllamaApiClient(http, pick);
            services.AddEmbeddingGenerator(defaultEmbed).UseLogging();
        }

    }
}
