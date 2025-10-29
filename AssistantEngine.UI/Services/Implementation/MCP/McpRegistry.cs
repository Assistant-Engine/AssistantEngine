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


public async Task<McpClientRegistration> RegisterAsync(McpConnectorConfig cfg)
    {
        // build transport for HTTP/S (SSE / streamable HTTP)
        var transportOptions = new SseClientTransportOptions
        {
            Endpoint = new Uri(cfg.ServerUrl),
            Name = cfg.Id,
            AdditionalHeaders = string.IsNullOrWhiteSpace(cfg.AuthToken)
                ? null
                : new Dictionary<string, string>
                {
                { "Authorization", $"Bearer {cfg.AuthToken}" }
                }
        };

        var transport = new SseClientTransport(transportOptions);

        // optional: you can pass McpClientOptions and a loggerFactory,
        // but minimal works with just transport.
        var client = await McpClientFactory.CreateAsync(transport);

        // fetch tools from server
        var tools = await client.ListToolsAsync().ConfigureAwait(false);
        // tools is IList<McpClientTool>, each one already implements AIFunction. :contentReference[oaicite:2]{index=2}

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
