using AssistantEngine.UI.Services;
using AssistantEngine.UI.Services.Implementation.Config;
using AssistantEngine.UI.Services.Options;
using Microsoft.Extensions.DependencyInjection;

namespace AssistantEngine.UI.Config
{
    public static class ServiceCollectionExtensions_Config
    {
        public static IServiceCollection AddAssistantEngineConfig(
            this IServiceCollection services,
            string defaultDataRoot,
            string? configFilePath = null)
        {
            var cfgPath = configFilePath ?? Path.Combine(defaultDataRoot, "App_Data", "appsettings.AssistantEngine.json");
            Directory.CreateDirectory(Path.GetDirectoryName(cfgPath)!);

            services.AddSingleton(new ConfigStorageOptions
            {
                DefaultDataRoot = defaultDataRoot,
                ConfigFilePath = cfgPath,
            });
            services.AddSingleton<IAppConfigStore, AppConfigStore>();
            return services;
        }
    }
}

