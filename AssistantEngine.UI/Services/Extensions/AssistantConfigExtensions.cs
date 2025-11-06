using AssistantEngine.Services.Implementation;
using AssistantEngine.Services.Implementation.Tools;
using AssistantEngine.UI.Services.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;

namespace AssistantEngine.UI.Services.Extensions
{
    public static class AssistantConfigExtensions
    {

        
            public static AssistantConfig Clone(this AssistantConfig s)
            {
                if (s is null) return new AssistantConfig();

                return new AssistantConfig
                {
                    Default = s.Default,
                    Name = s.Name,
                    Version = s.Version,
                    Id = s.Id,

                    ModelOptions = s.ModelOptions?.Select(m => m.Clone()).ToList() ?? new(),
                    McpConnectors = s.McpConnectors?.Select(c => c.Clone()).ToList() ?? new(),
                    Databases = s.Databases?.Select(d => d.Clone()).ToList() ?? new(),

                    ModelProvider = s.ModelProvider,
                    ModelProviderUrl = s.ModelProviderUrl,
                    SystemPrompt = s.SystemPrompt,

                    VectorStore = s.VectorStore,

                    IngestionPaths = s.IngestionPaths?.Select(p => new IngestionSourceFolder
                    {
                        Path = p.Path,
                        ExploreSubFolders = p.ExploreSubFolders,
                        FileExtensions = p.FileExtensions != null ? new List<string>(p.FileExtensions) : null
                    }).ToList() ?? new(),

                    EnabledFunctions = s.EnabledFunctions != null
                        ? s.EnabledFunctions.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                        : new List<string>(),

                    Description = s.Description,
                    EnableThinking = s.EnableThinking,
                    PersistThoughtHistory = s.PersistThoughtHistory
                };
            }

            public static NamedModelOption Clone(this NamedModelOption m) => new NamedModelOption
            {
                Key = m.Key,
                Label = m.Label,
                Options = m.Options?.CloneWithoutTools()
            };

            public static ChatOptions CloneWithoutTools(this ChatOptions o)
            {
                if (o is null) return new ChatOptions();

                return new ChatOptions
                {
                    ModelId = o.ModelId,
                    Temperature = o.Temperature,
                    TopP = o.TopP,
                    TopK = o.TopK,
                    MaxOutputTokens = o.MaxOutputTokens,
                    PresencePenalty = o.PresencePenalty,
                    FrequencyPenalty = o.FrequencyPenalty,
                    StopSequences = o.StopSequences?.ToArray(),
                    // Tools intentionally NOT copied to avoid shared state; UI rebinds tools per active config.
                };
            }
        
    

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
            var options = config.AssistantModel;
            var finalTools = new List<AIFunction>();

            static string Canonical(string name)
            {
                var core = name.Contains('/') ? name.Split('/')[^1] : name;
                if (core.EndsWith("Async", StringComparison.OrdinalIgnoreCase)) core = core[..^5];
                return core;
            }

            HashSet<string>? allowExact = null, allowCanonical = null;
            if (config.EnabledFunctions is { Count: > 0 })
            {
                allowExact = new HashSet<string>(config.EnabledFunctions, StringComparer.OrdinalIgnoreCase);
                allowCanonical = new HashSet<string>(config.EnabledFunctions.Select(Canonical), StringComparer.OrdinalIgnoreCase);
            }

            // 1) Native tools
            {
                var reflected = services.GetServices<ITool>()
                    .SelectMany(t => t.GetType()
                        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Where(m => m.GetCustomAttribute<DescriptionAttribute>() != null)
                        .Select(m => AIFunctionFactory.Create(m, t, "Native/" + m.Name)))
                    .ToList();

                if (allowExact is not null && allowCanonical is not null)
                    reflected = reflected
                        .Where(f => allowExact.Contains(f.Name) || allowCanonical.Contains(Canonical(f.Name)))
                        .ToList();

                finalTools.AddRange(reflected);
            }

            // 2) MCP tools
            {
                var mcpRegistry = services.GetService<IMcpRegistry>();
                if (mcpRegistry != null && config.McpConnectors != null)
                {
                    foreach (var connector in config.McpConnectors)
                    {
                        var reg = mcpRegistry.Get(connector.Id);
                        if (reg == null) continue;

                        HashSet<string>? mcpAllowExact = null, mcpAllowCanonical = null;
                        if (connector.EnabledTools is { Count: > 0 })
                        {
                            mcpAllowExact = new HashSet<string>(connector.EnabledTools, StringComparer.OrdinalIgnoreCase);
                            mcpAllowCanonical = new HashSet<string>(connector.EnabledTools.Select(Canonical), StringComparer.OrdinalIgnoreCase);
                        }

                        foreach (var mcpTool in reg.Tools)
                        {
                            if (mcpAllowExact is not null && mcpAllowCanonical is not null)
                            {
                                var exactOk = mcpAllowExact.Contains(mcpTool.Name);
                                var canonOk = mcpAllowCanonical.Contains(Canonical(mcpTool.Name));
                                if (!exactOk && !canonOk) continue;
                            }

                            finalTools.Add(mcpTool.WithName($"MCP/{connector.Id}/{mcpTool.Name}"));
                        }
                    }
                }
            }

            options.Tools = finalTools
                .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToArray();
            return options;
        }

    }
}
