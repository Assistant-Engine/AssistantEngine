using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssistantEngine.UI.Services.Extensions
{


    public static class ToolExtensions
    {
        public static List<AITool> GetNativeTools(this IList<AITool> tools)
            => (tools?.ToArray() ?? Array.Empty<AITool>())
               .Where(t => t?.Name != null && t.Name.StartsWith("Native/", StringComparison.OrdinalIgnoreCase))
               .ToList();

        public static List<AITool> GetMcpTools(this IList<AITool> tools)
            => (tools?.ToArray() ?? Array.Empty<AITool>())
               .Where(t => t?.Name != null && t.Name.StartsWith("MCP/", StringComparison.OrdinalIgnoreCase))
               .ToList();
    }

}
