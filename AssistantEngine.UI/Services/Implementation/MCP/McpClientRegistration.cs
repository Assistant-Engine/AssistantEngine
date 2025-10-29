using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
namespace AssistantEngine.UI.Services.Implementation.MCP
{

    public sealed class McpClientRegistration : IAsyncDisposable
    {
        public string Id { get; init; } = default!;
        public IMcpClient Client { get; init; } = default!;
        public IReadOnlyList<McpClientTool> Tools { get; init; } = Array.Empty<McpClientTool>();

        public ValueTask DisposeAsync() => Client.DisposeAsync();
    }
}
