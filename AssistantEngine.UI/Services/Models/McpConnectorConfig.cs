using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssistantEngine.UI.Services.Models
{
    public class McpConnectorConfig
    {
        public string Id { get; set; } = "New Connector";
        public string ServerUrl { get; set; } = "http://localhost:4001";
        public string? AuthToken { get; set; }

        // which tool names are allowed for this assistant
        public List<string> EnabledTools { get; set; } = new();
    }

}
