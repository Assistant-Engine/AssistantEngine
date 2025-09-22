using System.Collections.Concurrent;
using System.Net.Http;
using OllamaSharp;
using OllamaSharp.Models;
using Microsoft.Extensions.AI;
using AssistantEngine.UI.Services.Models;

namespace AssistantEngine.UI.Services.Implementation.Ollama
{
    public interface IOllamaClientResolver
    {
        IOllamaApiClient For(AssistantConfig cfg, string? modelId = null);
        Task<IReadOnlyList<Model>> ListLocalModelsAsync(AssistantConfig cfg, CancellationToken ct = default);
    }

    public sealed class OllamaClientResolver : IOllamaClientResolver
    {
        private readonly ConcurrentDictionary<string, OllamaApiClient> _byKey = new(StringComparer.OrdinalIgnoreCase);

        public IOllamaApiClient For(AssistantConfig cfg, string? modelId = null)
        {
            var url = string.IsNullOrWhiteSpace(cfg.ModelProviderUrl)
                ? "http://localhost:11434"
                : cfg.ModelProviderUrl.TrimEnd('/');

            var key = $"{url}::{modelId ?? ""}";

            return _byKey.GetOrAdd(key, _ =>
            {
                var uri = new Uri(url);
                // Prefer ctor that binds model; avoid mutating SelectedModel on a shared instance
                return string.IsNullOrWhiteSpace(modelId)
                    ? new OllamaApiClient(uri)
                    : new OllamaApiClient(uri, modelId);
            });
        }
        public async Task<IReadOnlyList<Model>> ListLocalModelsAsync(AssistantConfig cfg, CancellationToken ct = default)
        {
            var client = (OllamaApiClient)For(cfg);
            try
            {
                var list = await client.ListLocalModelsAsync(ct);
                return list.ToList();
            }
            catch (HttpRequestException) { return Array.Empty<Model>(); }
            catch (TaskCanceledException) { return Array.Empty<Model>(); }
            catch (Exception) { return Array.Empty<Model>(); }
        }
    }
}
