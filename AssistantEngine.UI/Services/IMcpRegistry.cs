using AssistantEngine.UI.Services.Implementation.MCP;
using AssistantEngine.UI.Services.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssistantEngine.UI.Services
{
    public interface IMcpRegistry
    {
        event Action<McpClientRegistration>? ConnectorAdded;
        event Action<string>? ConnectorRemoved; // by Id

        IEnumerable<McpClientRegistration> All { get; }

        McpClientRegistration? Get(string id);

        Task<McpClientRegistration> RegisterAsync(McpConnectorConfig cfg);
        void Remove(string id);
    }

}
