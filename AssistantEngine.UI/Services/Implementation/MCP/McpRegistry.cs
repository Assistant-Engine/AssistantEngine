using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AssistantEngine.UI.Services.Models;
using ModelContextProtocol.Client;
using Microsoft.Extensions.Logging;


using ModelContextProtocol.Client;
namespace AssistantEngine.UI.Services.Implementation.MCP
{
    using AssistantEngine.UI.Services.Extensions;
    using System.Collections.Concurrent;

    public class McpRegistry : IMcpRegistry
    {
        private readonly ConcurrentDictionary<string, McpClientRegistration> _map =
            new(StringComparer.OrdinalIgnoreCase);

        public event Action<McpClientRegistration>? ConnectorAdded;
        public event Action<string>? ConnectorRemoved;

        public IEnumerable<McpClientRegistration> All => _map.Values;

        public McpClientRegistration? Get(string id)
            => _map.TryGetValue(id, out var v) ? v : null;

        public static async Task<bool> EnsureFreshAccessTokenAsync(McpConnectorConfig cfg, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(cfg.OAuthTokenUrl) || string.IsNullOrWhiteSpace(cfg.OAuthRefreshToken))
                return true;

            if (cfg.OAuthExpiryUtc.HasValue && cfg.OAuthExpiryUtc.Value > DateTime.UtcNow.AddMinutes(1))
                return true;

            using var http = new HttpClient();

            var dict = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = cfg.OAuthRefreshToken!,
                ["client_id"] = cfg.OAuthClientId ?? "assistant-engine"
            };

            // NEW: confidential clients may require client_secret on refresh
            if (cfg.ClientMode == ClientMode.UserSuppliedConfidential && !string.IsNullOrWhiteSpace(cfg.OAuthClientSecret))
                dict["client_secret"] = cfg.OAuthClientSecret!;

            using var form = new FormUrlEncodedContent(dict);
            var resp = await http.PostAsync(cfg.OAuthTokenUrl, form, ct);
            if (!resp.IsSuccessStatusCode) return false;

            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var root = doc.RootElement;

            var at = root.TryGetProperty("access_token", out var atEl) ? atEl.GetString() : null;
            var rt = root.TryGetProperty("refresh_token", out var rtEl) ? rtEl.GetString() : null;
            var exp = root.TryGetProperty("expires_in", out var ei) && ei.TryGetInt32(out var secs) ? secs : (int?)null;

            if (string.IsNullOrWhiteSpace(at)) return false;

            cfg.OAuthAccessToken = at;
            if (!string.IsNullOrWhiteSpace(rt)) cfg.OAuthRefreshToken = rt;
            cfg.OAuthExpiryUtc = exp.HasValue ? DateTime.UtcNow.AddSeconds(exp.Value) : (DateTimeOffset?)null;

            return true;
        }

        public async Task<McpClientRegistration> RegisterAsync(McpConnectorConfig cfg)
        {
            // ensure token is fresh (no-op if not OAuth or not expiring)
            await EnsureFreshAccessTokenAsync(cfg);

            var transportOptions = new SseClientTransportOptions
            {
                Endpoint = new Uri(cfg.ServerUrl),
                Name = cfg.Id,
                AdditionalHeaders = McpAuthExtensions.BuildAuthHeaders(cfg)
            };

            var transport = new SseClientTransport(transportOptions);
            var client = await McpClientFactory.CreateAsync(transport);

            var tools = await client.ListToolsAsync().ConfigureAwait(false);

            var reg = new McpClientRegistration
            {
                Id = cfg.Id,
                Client = client,
                Tools = tools.ToList()
            };

            _map[cfg.Id] = reg;
            ConnectorAdded?.Invoke(reg);
            return reg;
        }

        public void Remove(string id)
        {
            if (_map.TryRemove(id, out var reg))
            {
                reg.DisposeAsync().AsTask().ConfigureAwait(false);
                ConnectorRemoved?.Invoke(id);
            }
        }
    }

}
