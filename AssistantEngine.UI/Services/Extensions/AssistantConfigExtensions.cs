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
    }
}
