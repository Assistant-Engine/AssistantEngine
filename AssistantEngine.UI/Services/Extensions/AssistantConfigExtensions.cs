using AssistantEngine.Services.Implementation;
using AssistantEngine.Services.Implementation.Tools;
using AssistantEngine.UI.Services.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Reflection;

namespace AssistantEngine.UI.Services.Extensions
{
    public static class AssistantConfigExtensions
    {
        /// <summary>
        /// Builds ChatOptions from the config and injects only the EnabledFunctions as AIFunctions.
        /// </summary>
        public static ChatOptions WithEnabledTools(this AssistantConfig config, IServiceProvider services)
        {
            var options = config.AssistantModel;

            if (config.EnabledFunctions is null || config.EnabledFunctions.Count == 0)
                return options;

            var tools = services.GetServices<ITool>();

            var funcs = tools
                .SelectMany(tool => tool.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.GetCustomAttribute<DescriptionAttribute>() != null)
                    .Select(m => AIFunctionFactory.Create(m, tool)))
                .Where(f => config.EnabledFunctions.Contains(f.Name))
                .ToArray();

            options.Tools = funcs;
            return options;
        }

        public static ChatOptions WithEnabledToolsAndMcp(this AssistantConfig config, IServiceProvider services)
        {
            // Start from the model's ChatOptions
            var options = config.AssistantModel;

            // We'll build one combined tool list
            var finalTools = new List<AIFunction>();

            // 1. Built-in .NET tools (ITool reflection)
            {
                var allLocalTools = services.GetServices<ITool>();

                var reflected = allLocalTools
                    .SelectMany(tool => tool.GetType()
                        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Where(m => m.GetCustomAttribute<DescriptionAttribute>() != null)
                        .Select(m => AIFunctionFactory.Create(m, tool))
                    )
                    .ToList();

                // If config.EnabledFunctions is set,
                // treat it as an allowlist for these reflected functions
                if (config.EnabledFunctions is { Count: > 0 })
                {
                    reflected = reflected
                        .Where(f => config.EnabledFunctions.Contains(f.Name))
                        .ToList();
                }

                finalTools.AddRange(reflected);
            }

            // 2. MCP connector tools
            {
                var mcpRegistry = services.GetService<IMcpRegistry>();
                // if there's no registry (edge case in tests), skip safely
                if (mcpRegistry != null && config.McpConnectors != null)
                {
                    foreach (var connector in config.McpConnectors)
                    {
                        // grab the live registration (created when you did DiscoverTools / RegisterAsync)
                        var reg = mcpRegistry.Get(connector.Id);
                        if (reg == null) continue;

                        // reg.Tools is IReadOnlyList<McpClientTool>
                        // McpClientTool already implements AIFunction, so we can add directly
                        foreach (var mcpTool in reg.Tools)
                        {
                            // allowlist per connector.EnabledTools
                            if (connector.EnabledTools.Contains(mcpTool.Name))
                            {
                                finalTools.Add(mcpTool);
                            }
                        }
                    }
                }
            }

            options.Tools = finalTools.ToArray();
            return options;
        }

    }
}
