using AssistantEngine.Factories;
using AssistantEngine.Services.Implementation;
using AssistantEngine.Services.Implementation.Tools;
using AssistantEngine.UI.Services.Implementation.Chat;
using AssistantEngine.UI.Services.Implementation.Config;
using AssistantEngine.UI.Services.Implementation.Database;
using AssistantEngine.UI.Services.Implementation.Factories;
using AssistantEngine.UI.Services.Implementation.Health;
using AssistantEngine.UI.Services.Implementation.Ingestion;
using AssistantEngine.UI.Services.Implementation.Ingestion.Chunks;
using AssistantEngine.UI.Services.Implementation.Ingestion.Embedding;
using AssistantEngine.UI.Services.Implementation.Ollama;
using AssistantEngine.UI.Services.Implementation.Startup;
using AssistantEngine.UI.Services.Models;
using AssistantEngine.UI.Services.Models.Ingestion;
using AssistantEngine.UI.Services.Options;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using System.Collections.Concurrent;

namespace AssistantEngine.UI.Services;

public static class DependencyInjection
{
    public static void RunAssistantEngineStartup(this IServiceProvider services)
    {
        AssistantEngine.UI.Services.Implementation.Startup.StartupInit.FireAndForget(services);
    }
    public static IServiceCollection AddAssistantEngineCore(
        this IServiceCollection services,
      //  IAppConfigStore cfgStore,
           ConfigStorageOptions options,
        bool noInternetMode = false,
        TimeSpan? embedResolveTimeout = null)
    {
        var appConfigStore = new AssistantEngine.UI.Services.Implementation.Config.AppConfigStore(options);
        AssistantEngine.UI.Services.Implementation.Config.ConfigSeeder.SeedShippedModelsIntoAppData(appConfigStore);

        services.AddSingleton<IAppConfigStore>(appConfigStore);

        // Model config store (source of truth)
        var store = new JsonAssistantConfigStore(appConfigStore.Current.ModelFilePath);
        services.AddSingleton<IAssistantConfigStore>(store);

        // Pull configs from store and ensure single default
        var modelConfigs = store.GetAll().ToList();
        if (modelConfigs.Count == 0)
        {
            // Minimal placeholder so the app can boot; UI can guide the user.
            modelConfigs.Add(new AssistantConfig { Id = "InitialModel", Name = "InitialModel", Default = true });
        }
        EnsureSingleDefault(modelConfigs);

        services.AddSingleton<IEnumerable<AssistantConfig>>(modelConfigs);
        foreach (var cfg in modelConfigs) services.AddSingleton(cfg);

        // Tools + http
        services.AddHttpClient<WebSearchTool>();
        services.Scan(scan => scan
            .FromAssemblyOf<ITool>()
            .AddClasses(c => c.InNamespaces("AssistantEngine.Services.Implementation.Tools"))
            .AsImplementedInterfaces()
            .WithTransientLifetime());

        // Chat / Ollama client factories
        services.AddSingleton<IOllamaClientResolver, OllamaClientResolver>();
        services.AddSingleton<IChatRepository, FileChatRepository>();

        /*services.AddScoped<ChatClientFactory>(sp => modelId =>
        {
            var state = sp.GetRequiredService<ChatClientState>();
            var resolver = sp.GetRequiredService<IOllamaClientResolver>();
            return (IChatClient)resolver.For(state.Config, modelId);
        });*/
        services.AddScoped<ChatClientFactory>(sp => modelId =>
        {
            var state = sp.GetRequiredService<ChatClientState>();
            var resolver = sp.GetRequiredService<IOllamaClientResolver>();
            var client = (IChatClient)resolver.For(state.Config, modelId);
            return client
                .AsBuilder()
                .UseFunctionInvocation()
                .Build();
        });
        services.AddScoped<OllamaClientFactory>(sp => modelId =>
        {
            var state = sp.GetRequiredService<ChatClientState>();
            var resolver = sp.GetRequiredService<IOllamaClientResolver>();
            return resolver.For(state.Config, modelId);
        });

        services.AddScoped<EmbedClientFactory>(sp => modelId =>
        {
            var state = sp.GetRequiredService<ChatClientState>();
            var resolver = sp.GetRequiredService<IOllamaClientResolver>();
            return (IEmbeddingGenerator<string, Embedding>)resolver.For(state.Config, modelId);
        });

        services.AddScoped<IToolStatusNotifier, ToolStatusNotifier>();
        services.AddScoped<ChatClientState>();
        services.AddScoped<Func<AssistantConfig>>(sp =>
        {
            var state = sp.GetRequiredService<ChatClientState>();
            var s = sp.GetRequiredService<IAssistantConfigStore>();
            return () => state.Config ?? s.GetAll().First();
        });

        // Health + embeddings
        var health = new AppHealthService();
        services.AddSingleton<IAppHealthService>(health);
        var def = modelConfigs.FirstOrDefault(m => m.Default) ?? modelConfigs.First();
        try
        {
            EmbeddingBootstrap.RegisterDefaultEmbedding(
                services, modelConfigs,
                timeout: embedResolveTimeout ?? TimeSpan.FromSeconds(15),
                onResolved: resolvedModel =>
                    health.SetStatus(
                        HealthDomain.Ollama,
                        HealthLevel.Healthy,
                        error: null,
                        detail: "Ollama reachable and embedding resolved.",
                        meta: new Dictionary<string, string>
                        {
                            ["ServerUrl"] = def.ModelProviderUrl,
                            ["EmbeddingModel"] = resolvedModel
                        }));
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.StackTrace);
            // Don’t crash the app if Ollama isn’t available at launch
            health.SetStatus(
                HealthDomain.Ollama,
                HealthLevel.Degraded,
                error: ex.Message,
                detail: "Ollama not reachable at startup; continuing without embeddings.");
        }

        if (!noInternetMode)
            services.AddSingleton<IDatabaseRegistry, DatabaseRegistry>();

        // Vector store (SQLite)
        var conn = $"Data Source={appConfigStore.Current.VectorStoreFilePath}";
        //does this match others this is equal to
       
        services.AddSqliteCollection<string, IngestedTextChunk>("text-chunks", conn);
        services.AddSqliteCollection<string, IngestedSQLTableChunk>("sql-table-chunks", conn);
        services.AddSqliteCollection<string, IngestedCodeChunk>("code-chunks", conn);
        services.AddSqliteCollection<string, IngestedDocument>("data-echoed-documents", conn);

        services.AddSingleton<IChunkStoreFactory>(sp =>
        {
            var map = new ConcurrentDictionary<string, Func<IServiceProvider, IChunkStore>>(StringComparer.OrdinalIgnoreCase);
            map["sql-table-chunks"] = isp => new ChunkStoreAdapter<IngestedSQLTableChunk>(isp.GetRequiredService<VectorStoreCollection<string, IngestedSQLTableChunk>>());
            map["text-chunks"] = isp => new ChunkStoreAdapter<IngestedTextChunk>(isp.GetRequiredService<VectorStoreCollection<string, IngestedTextChunk>>());
            map["code-chunks"] = isp => new ChunkStoreAdapter<IngestedCodeChunk>(isp.GetRequiredService<VectorStoreCollection<string, IngestedCodeChunk>>());
            return new ChunkStoreFactory(sp, map);
        });

        services.AddMemoryCache();
        services.AddHttpClient<OllamaImportService>();
        services.AddScoped<DataIngestor>();
        services.AddScoped<SemanticSearch>();

        // Shared startup initializer
        services.AddSingleton<IStartupInitializer, StartupInitializer>();

        return services;
    }

    private static void EnsureSingleDefault(List<AssistantConfig> list)
    {
        if (!list.Any(m => m.Default)) { list[0].Default = true; return; }
        bool seen = false;
        foreach (var m in list.Where(m => m.Default))
        {
            if (!seen) { seen = true; continue; }
            m.Default = false;
        }
    }
}
