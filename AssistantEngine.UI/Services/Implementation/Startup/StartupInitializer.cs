using AssistantEngine.UI.Services.AppDatabase;
using AssistantEngine.UI.Services.Models.Ingestion;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

namespace AssistantEngine.UI.Services.Implementation.Startup;


public static class StartupInit
{
    public static void FireAndForget(IServiceProvider root)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = root.CreateScope();
                var init = scope.ServiceProvider.GetRequiredService<IStartupInitializer>();
                await init.RunAsync();
            }
            catch(Exception ex) {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                   /* swallow: app must not be blocked */ }
        });
    }
}

public interface IStartupInitializer
{
    Task RunAsync(CancellationToken ct = default);
}
public sealed class StartupInitializer : IStartupInitializer
{
    private static volatile bool _ran;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly IServiceProvider _sp;
    private readonly ILogger<StartupInitializer> _log;

    public StartupInitializer(IServiceProvider sp, ILogger<StartupInitializer> log)
    {
        _sp = sp;
        _log = log;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        if (_ran) return;
        await Gate.WaitAsync(ct);
        try
        {
            if (_ran) return;

            using var scope = _sp.CreateScope();
            var svc = scope.ServiceProvider;
            var healthSvc = svc.GetRequiredService<IAppHealthService>();
            var ollama = healthSvc.Get(HealthDomain.Ollama);

           /* try
            {
                var appCfg = svc.GetRequiredService<IAppConfigStore>();
                var dbPath = appCfg.Current.AppDBFilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

                var cs = new SqliteConnectionStringBuilder
                {
                    DataSource = dbPath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Cache = SqliteCacheMode.Shared
                }.ToString();

                await AppDbInitializer.EnsureSchemaAsync(cs);

                // Optional: mark health
                healthSvc.SetStatus(HealthDomain.Database, HealthLevel.Healthy, detail: "App DB ensured.");
            }
            catch (Exception ex)
            {
                healthSvc.SetStatus(HealthDomain.Database, HealthLevel.Unhealthy, error: ex.Message, detail: "App DB init failed.");
                _log.LogError(ex, "[Init] App DB ensure failed");
            }*/
            if (ollama.Level == HealthLevel.Healthy)
            {
                try
                {
                    await svc.GetRequiredService<VectorStoreCollection<string, IngestedTextChunk>>()
                             .EnsureCollectionExistsAsync(ct);
                    await svc.GetRequiredService<VectorStoreCollection<string, IngestedSQLTableChunk>>()
                             .EnsureCollectionExistsAsync(ct);
                    await svc.GetRequiredService<VectorStoreCollection<string, IngestedDocument>>()
                             .EnsureCollectionExistsAsync(ct);
                    await svc.GetRequiredService<VectorStoreCollection<string, IngestedCodeChunk>>()
                             .EnsureCollectionExistsAsync(ct);

                    healthSvc.SetStatus(HealthDomain.VectorStore, HealthLevel.Healthy, detail: "Collections ensured.");
                }
                catch (Exception ex)
                {
                    healthSvc.SetStatus(HealthDomain.VectorStore, HealthLevel.Unhealthy, error: ex.Message, detail: "Ensuring collections failed.");
                    _log.LogError(ex, "[Init] EnsureCollectionExists failed");
                }
            }
            else
            {
                healthSvc.SetStatus(HealthDomain.VectorStore, HealthLevel.Degraded, error: "Ollama not connected.", detail: "Skipped EnsureCollectionExists.");
                _log.LogWarning("[Init] Skipped EnsureCollectionExists: Ollama not connected.");
            }

            var repo = svc.GetRequiredService<IChatRepository>();
            await repo.InitializeAsync();
            _log.LogInformation("[Init] Chats: {Names}", string.Join(", ", repo.ChatSessionNames));

            _ran = true;
        }
        finally
        {
            Gate.Release();
        }
    }
}
