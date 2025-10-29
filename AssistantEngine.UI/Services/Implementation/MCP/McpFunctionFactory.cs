using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;

namespace AssistantEngine.UI.Services.Implementation.MCP
{


    public static class McpFunctionFactory
    {
        public static AIFunction Create(McpClientRegistration reg, McpClientTool tool) //not used
        {
            return tool;
        }
        /* return AIFunctionFactory.Create(
             name: tool.Name,
             description: tool.Description ?? $"MCP tool {tool.Name} from {reg.Id}",
             parametersSchema: tool.JsonSchema, // if you have it, else null
             function: async (IDictionary<string, object?> args, CancellationToken ct) =>
             {
                 // call the MCP tool
                 var result = await reg.Client.CallToolAsync(tool.Name, args, ct)
                                             .ConfigureAwait(false);

                 // return simple string back to model
                 return result?.ToString() ?? string.Empty;
             });
     }*/
    }

}
