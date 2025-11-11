using AssistantEngine.Factories;
using AssistantEngine.Services.Implementation;
using AssistantEngine.UI.Services.Types;
using System.Collections.Concurrent;
namespace AssistantEngine.UI.Services.Implementation.Ollama
{




    public sealed class OllamaModelInstaller : IModelInstaller
    {
        private readonly ChatClientState _state;                  // for IOllamaApiClient
        private readonly IToolStatusNotifier _notifier;           // for StatusMessage(...)
        private static readonly ConcurrentDictionary<string, byte> _pulling =
            new(StringComparer.OrdinalIgnoreCase);

        public OllamaModelInstaller(ChatClientState state, IToolStatusNotifier notifier)
        {
            _state = state;
            _notifier = notifier;
        }

        public async Task<bool> IsInstalledAsync(string model, CancellationToken ct = default)
        {
            if (!model.Contains(':')) model += ":latest";
            var local = await _state.OllamaClient.ListLocalModelsAsync(ct);
            return local.Any(x => string.Equals(x.Name, model, StringComparison.OrdinalIgnoreCase));
        }

        public async Task PullAsync(string model, CancellationToken ct = default)
        {
            if (await IsInstalledAsync(model, ct))
            {
                _notifier.StatusMessage(new StatusMessage($"{model} already installed", StatusLevel.Success, "Ollama"));
                return;
            }
            if (!_pulling.TryAdd(model, 0))
            {
                _notifier.StatusMessage(new StatusMessage($"{model} is already downloading", StatusLevel.Information, "Ollama"));
                return;
            }

            try
            {
                _notifier.StatusMessage(new StatusMessage($"Starting download: {model}", StatusLevel.Information, "Ollama"));

                var req = new OllamaSharp.Models.PullModelRequest { Model = model };
                await foreach (var _ in _state.OllamaClient.PullModelAsync(req).WithCancellation(ct).ConfigureAwait(false))
                {
                    // optional: parse progress events and emit StatusMessage with progress
                }

                if (await IsInstalledAsync(model, ct))
                    _notifier.StatusMessage(new StatusMessage($"Download complete: {model}", StatusLevel.Success, "Ollama"));
                else
                    _notifier.StatusMessage(new StatusMessage($"Download finished but not installed: {model}", StatusLevel.Warning, "Ollama"));
            }
            catch (OperationCanceledException) { /* optional */ }
            catch (Exception ex)
            {
                _notifier.StatusMessage(new StatusMessage($"Failed to download {model}: {ex.Message}", StatusLevel.Error, "Ollama"));
            }
            finally
            {
                _pulling.TryRemove(model, out _);
            }
        }
    }
}
