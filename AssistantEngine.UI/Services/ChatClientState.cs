using AssistantEngine.Services.Extensions;
using AssistantEngine.Services.Implementation;
using AssistantEngine.Services.Implementation.Tools;
using AssistantEngine.UI.Pages.Chat;
using AssistantEngine.UI.Services;
using AssistantEngine.UI.Services.Extensions;
using AssistantEngine.UI.Services.Implementation.Config;
using AssistantEngine.UI.Services.Implementation.Database;
using AssistantEngine.UI.Services.Implementation.Factories;
using AssistantEngine.UI.Services.Implementation.Ingestion;
using AssistantEngine.UI.Services.Implementation.MCP;
using AssistantEngine.UI.Services.Models;
using AssistantEngine.UI.Services.Models.Ingestion;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;
using OllamaSharp;
using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Net.NetworkInformation;
using System.Reflection;

namespace AssistantEngine.Factories
{
    public class SpinnerToggleEventArgs
    {
        public bool Visible { get; set; }
        public string? Message { get; set; }
    }

    // ChatClientState.cs
    public class ChatClientState : IDisposable
    {
        public void Dispose()
        {
            _notifier.OnStatusMessage -= StatusMessage;
            _dbRegistry.DatabaseAdded -= OnDatabaseAddedAsync;
            _dbRegistry.DatabaseRemoved -= OnDatabaseRemoved;
        }

        private readonly IAppConfigStore _config;
        private readonly IDatabaseRegistry _dbRegistry;
        readonly IToolStatusNotifier _notifier;
        readonly DataIngestor _dataIngestor;
        readonly ChatClientFactory _factory;
        readonly OllamaClientFactory _ollamaFactory;
        readonly IAssistantConfigStore _allConfigs;

        //readonly IOptionsMonitor<ChatOptions> _opts;
        // readonly IEnumerable<ITool> _tools;
        readonly IServiceProvider _services;
        // readonly IWebHostEnvironment _env;
        private readonly IAppHealthService _health;
        public ChatOptions ChatOptions { get; private set; }
        //  Dictionary<string, IDatabase> _databaseDict;

        //so next what does this neeed to do? it needs to embed a description.
        //we can inject the model.

        public ChatClientState(
            DataIngestor dataIngestor,
            IServiceProvider services,
            ChatClientFactory factory,
            IAppConfigStore config,
            OllamaClientFactory ollamaFactory,
            IDatabaseRegistry dbRegistry,
            IAssistantConfigStore configs,
            IOptionsMonitor<ChatOptions> opts,
            IToolStatusNotifier notifier,
             IAppHealthService health)
        {
            _health = health;
            _dbRegistry = dbRegistry;
            _dataIngestor = dataIngestor;
            _services = services;
            _config = config;
            _factory = factory;
            _allConfigs = configs;
            _ollamaFactory = ollamaFactory;
            _notifier = notifier;

            // ✅ ensure idempotent subscriptions
            _notifier.OnStatusMessage -= StatusMessage;
            _notifier.OnStatusMessage += StatusMessage;

            _dbRegistry.DatabaseAdded -= OnDatabaseAddedAsync;
            _dbRegistry.DatabaseAdded += OnDatabaseAddedAsync;

            _dbRegistry.DatabaseRemoved -= OnDatabaseRemoved;
            _dbRegistry.DatabaseRemoved += OnDatabaseRemoved;
        }

        // move lambdas into methods so you can unsubscribe cleanly
        private async void OnDatabaseAddedAsync(IDatabase db)
        {
            await IngestNewDatabaseAsync(db);
        }

        private void OnDatabaseRemoved(string id)
        {
            StatusMessage($"Database {id} removed – its chunks will no longer be available.");
            // optionally: purge from vector store here
        }
        private async Task IngestNewDatabaseAsync(IDatabase db)
        {
            try
            {
                var descriptorClient = _factory(Config.DescriptorModel.ModelId);
                var sqlSource = new DatabaseIngestionSource(db, _factory, Config);
                sqlSource.StatusMessage += LoaderMessage;

                await _dataIngestor.IngestDataAsync(
                    sqlSource,
                    "sql-table-chunks",
                    "data-echoed-documents"
                );

                StatusMessage($"Database {db.Configuration.Id} ingested.");
            }
            catch (Exception ex)
            {
                StatusMessage($"Error ingesting {db.Configuration.Id}: {ex.Message}");
            }
        }


        public void StatusMessage(string msg) => OnStatusMessage?.Invoke(msg);
        private void LoaderMessage(string msg) => OnLoaderMessage?.Invoke(true,msg);
        private void ChatLoaderMessage(string msg) => OnChatLoaderMessage?.Invoke(true,msg);
        public event Action<string>? OnStatusMessage;
        public event Action<bool, string?>? OnLoaderMessage;
        public event Action<bool, string?>? OnChatLoaderMessage;
        public event Action<string>? OnStatusEvent; //INGESTING
        readonly ConcurrentDictionary<string, Task> _ingestionTasks
    = new();
        public IChatClient Client { get;  set; }
        public IOllamaApiClient OllamaClient { get; private set; }
        public AssistantConfig Config { get; private set; }

        public bool IngestionFinished { get; private set; } = false;

        //this refers to ones like echo ed
        public void ChangeModel(string id)
        {
            try
            {

                var tools = _services.GetServices<ITool>();
            

                Config = _allConfigs.GetById(id);
                Client = _factory(Config.AssistantModel.ModelId);

              
                OllamaClient = _ollamaFactory(Config.AssistantModel.ModelId);
                //ChatOptions = _opts.Get(Config.AssistantModelId);
              
                ChatOptions = Config.WithEnabledToolsAndMcp(_services);

               

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
            }
    
            // ChatOptions.MergeFrom(Config.ChatOptions);

        }


        public async Task ChangeModelAsync(string id = "")
        {
            if (false/*!_health.Snapshot.OllamaConnected || _health.Snapshot.OllamaError != null*/)
            {
               // OnStatusMessage?.Invoke(_health.Snapshot.OllamaError ?? "Ollama not reachable. Model features disabled.");// either here DEFINITELY HERE
                //return;
            }
            if (string.IsNullOrEmpty(id))
            {
                id = _allConfigs.GetAll().FirstOrDefault(c => c.Default)?.Id ?? _allConfigs.GetAll().First().Id;
            }
            // pick config + clients
            Config = _allConfigs.GetAll().First(c => c.Id == id); // not set to an instance of an object once
            Client = _factory(Config.AssistantModel.ModelId);
            OllamaClient = _ollamaFactory(Config.AssistantModel.ModelId);

            // 👇 hydrate databases for this config
            foreach (var dbConfig in Config.Databases)
            {
                _dbRegistry.Register(dbConfig);
            }


            // hydrate MCP connectors for this config
            var mcp = _services.GetRequiredService<IMcpRegistry>();
            foreach (var conn in Config.McpConnectors)
            {
                // fire and forget connect
                _ = mcp.RegisterAsync(conn).ContinueWith(t =>
                {
                    if (t.Exception != null)
                    {
                        StatusMessage($"MCP '{conn.Id}' failed: {t.Exception.InnerException?.Message}");
                    }
                    else
                    {
                        StatusMessage($"MCP '{conn.Id}' connected.");
                    }
                });
            }



            // trigger or reuse ingestion
            OnStatusMessage?.Invoke($"Ingesting data for “{id}”…");
            var ingestTask = _ingestionTasks.GetOrAdd(id, _ => IngestDataAsync(Config));
            await ingestTask;
            IngestionFinished = true;
            OnStatusMessage?.Invoke($"Model “{id}” ready");

            // wire up tools…
            ChatOptions = Config.WithEnabledToolsAndMcp(_services);
    
            // start with built-in .NET tools (your ITool reflection path)
      

        }
        public void RefreshTools()
        {
            ChatOptions = Config.WithEnabledToolsAndMcp(_services);
        }
        public async Task ReingestDatabases()
        {
            if (Config?.Databases == null)
                return;

            foreach (var item in Config.Databases)
            {
                var db = _dbRegistry.Get(item.Id);
                if (db == null)
                {
                    StatusMessage($"Database {item.Id} not found in registry.");
                    continue;
                }

                try
                {


                    var dbSource = new DatabaseIngestionSource(db, _factory, Config);
                    dbSource.StatusMessage += StatusMessage;

                    await _dataIngestor.DeleteSourceAsync(
                        "data-echoed-documents", "sql-table-chunks", dbSource.SourceId);
                  
                  

                  
                }
                catch (Exception ex)
                {
                    StatusMessage($"Error reingesting database {item.Id}: {ex.Message}");
                }
            }
            await IngestDataAsync(Config);
        }

        public async Task ReingestDatabase(DatabaseConfiguration item)
        {
            var db = _dbRegistry.Get(item.Id);
            if (db == null)
            {
                StatusMessage($"Database {item.Id} not found in registry.");
               // continue;
            }

            try
            {
                // wipe old
                await _dataIngestor.DeleteSourceAsync(
                      "data-echoed-documents",
                    "sql-table-chunks",
                    item.Id
                );

                // re-ingest
                var sqlSource = new DatabaseIngestionSource(db, _factory, Config);
                sqlSource.StatusMessage += StatusMessage;

                await _dataIngestor.IngestDataAsync(
                    sqlSource,
                    "sql-table-chunks",
                    "data-echoed-documents"
                );
            }
            catch (Exception ex)
            {
                StatusMessage($"Error reingesting database {item.Id}: {ex.Message}");
            }
        }
        public async Task ReingestDocuments()
        {
            if (Config?.IngestionPaths == null)
                return;

            foreach (var item in Config.IngestionPaths)
            {
                if (!Directory.Exists(item.Path))
                {
                    StatusMessage($"Path '{item.Path}' does not exist – skipping.");
                    continue;
                }

                try
                {
                   
                    /*var csSource = new CSDirectorySource(item.Path, item.ExploreSubFolders);
                    csSource.StatusMessage += LoaderMessage;
                    var pdfSource = new PDFDirectorySource(item.Path, item.ExploreSubFolders);
                    pdfSource.StatusMessage += StatusMessage;

                    await _dataIngestor.DeleteSourceAsync(
                        "data-echoed-documents", "code-chunks", csSource.SourceId);

                    await _dataIngestor.DeleteSourceAsync(
                      "data-echoed-documents", "text-chunks", pdfSource.SourceId);*/
                    // re-ingest


                    var csSource = new CSDirectorySource(item.Path, item.ExploreSubFolders);
                    csSource.StatusMessage += LoaderMessage;

                    var pdfSource = new PDFDirectorySource(item.Path, item.ExploreSubFolders);
                    pdfSource.StatusMessage += LoaderMessage;

                    var generalSource = new GeneralDirectorySource(item.Path, item.ExploreSubFolders, item.FileExtensions);
                    generalSource.StatusMessage += LoaderMessage;

                    // Remove prior docs/chunks for each source
                    await _dataIngestor.DeleteSourceAsync(
                        "data-echoed-documents", "code-chunks", csSource.SourceId);

                    await _dataIngestor.DeleteSourceAsync(
                        "data-echoed-documents", "text-chunks", pdfSource.SourceId);

                    await _dataIngestor.DeleteSourceAsync(
                        "data-echoed-documents", "text-chunks", generalSource.SourceId);



                }
                catch (Exception ex)
                {
                    StatusMessage($"Error reingesting documents from {item.Path}: {ex.Message}");
                }
            }
            await IngestDataAsync(Config);
        }

       
        /// <summary>
        ///
        /// So regarding data imgestion the issue is this might take a while, and what if it is not ready, do we want to wait each time
        /// 
        /// we can have it part of the model process? i.e. the model does not completely load until the data is ingested?
        /// 
        /// or we dont? then what? it is ingesting in the background until is ready?
        /// 
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        public async Task IngestDataAsync(AssistantConfig config)
        {
            IngestionFinished = false;
            var o = _health.Get(HealthDomain.Ollama);
            if (o.Level != HealthLevel.Healthy)
            {
                StatusMessage(o.Error ?? "Ollama not reachable; skipping ingestion.");
                return;
            }

            foreach (var item in config.IngestionPaths)
            {
                if (!Directory.Exists(item.Path))
                           {
                    LoaderMessage($"Path '{item.Path}' does not exist – skipping.");
                              continue;
                          }
                try
                {
                    var csSource = new CSDirectorySource(item.Path, item.ExploreSubFolders);
                    csSource.StatusMessage += LoaderMessage;
                    await _dataIngestor.IngestDataAsync(csSource, "code-chunks", "data-echoed-documents");

                   /* var codeChunksCount = await _dataIngestor.CountChunksAsync("code-chunks");
                    var csDocsCount = await _dataIngestor.CountDocumentsAsync("data-echoed-documents", csSource.SourceId);
                    LoaderMessage($"code-chunks total: {codeChunksCount}");
                    LoaderMessage($"docs for {csSource.SourceId}: {csDocsCount}");*/


                    var generalSource = new GeneralDirectorySource(item.Path, item.ExploreSubFolders, item.FileExtensions);
                    generalSource.StatusMessage += LoaderMessage;
                    await _dataIngestor.IngestDataAsync(generalSource, "text-chunks", "data-echoed-documents");

                    /*var textChunksCountGeneral = await _dataIngestor.CountChunksAsync("text-chunks");
                    var generalDocsCount = await _dataIngestor.CountDocumentsAsync("data-echoed-documents", generalSource.SourceId);
                    LoaderMessage($"text-chunks total: {textChunksCountGeneral}");
                    LoaderMessage($"docs for {generalSource.SourceId}: {generalDocsCount}");*/


                    var pdfSource = new PDFDirectorySource(item.Path, item.ExploreSubFolders);
                    pdfSource.StatusMessage += LoaderMessage;
                    await _dataIngestor.IngestDataAsync(pdfSource, "text-chunks", "data-echoed-documents");

                    /*var textChunksCountPDF = await _dataIngestor.CountChunksAsync("text-chunks");
                    var pdfDocsCount = await _dataIngestor.CountDocumentsAsync("data-echoed-documents", pdfSource.SourceId);
                    LoaderMessage($"text-chunks total (after PDFs): {textChunksCountPDF}");
                    LoaderMessage($"docs for {pdfSource.SourceId}: {pdfDocsCount}");*/


                    var finalCodeCount = await _dataIngestor.CountChunksAsync("code-chunks");
                    var finalTextCount = await _dataIngestor.CountChunksAsync("text-chunks");
                    var finalSqlCount = await _dataIngestor.CountChunksAsync("sql-table-chunks");

                   // LoaderMessage($"Final totals: code-chunks={finalCodeCount}, text-chunks={finalTextCount}, sql-table-chunks={finalSqlCount}");


                }
                catch (Exception ex)
                {
                    LoaderMessage($"Error ingesting '{item.Path}': {ex.Message}");
                    Console.WriteLine($"Error processing path {item.Path}: {ex.Message}");
                    continue; // skip this item and continue with the next
                }
  
            }
            foreach (var item in config.Databases)
            {
                var db = _dbRegistry.Get(item.Id);
                if (db != null)
                {
                    var descriptorClient = _factory(Config.DescriptorModel.ModelId);
                    var sqlSource = new DatabaseIngestionSource(db, _factory, Config);
                    sqlSource.StatusMessage += LoaderMessage;
                    try
                    {
                        await _dataIngestor.IngestDataAsync(
                        sqlSource,
                        "sql-table-chunks",
                        "data-echoed-documents"
                    );
                    }catch(Exception ex)
                    {
                        LoaderMessage($"Error ingesting {ex.Message}");

                    }
                }
            }
            IngestionFinished = true;
        }
        // call this whenever you want to switch the active client
        public void ChangeAssistantModel(string id)
        {
            Client = _factory(id);
            OllamaClient = _ollamaFactory(id);
        }
        // ADD inside ChatClientState
        private async Task ForceCreateCollectionsAsync()
        {
            var textChunks = _services.GetRequiredService<VectorStoreCollection<string, IngestedTextChunk>>();
            var sqlChunks = _services.GetRequiredService<VectorStoreCollection<string, IngestedSQLTableChunk>>();
            var codeChunks = _services.GetRequiredService<VectorStoreCollection<string, IngestedCodeChunk>>();
            var documents = _services.GetRequiredService<VectorStoreCollection<string, IngestedDocument>>();

            // Touch each collection to force schema creation after file delete
            await textChunks.GetAsync(x => true, 1).ToListAsync();
            await sqlChunks.GetAsync(x => true, 1).ToListAsync();
            await codeChunks.GetAsync(x => true, 1).ToListAsync();
            await documents.GetAsync(x => true, 1).ToListAsync();
        }


        public async Task HardWipeVectorStoresAsync(string sqlitePath)
        {
            await SoftWipeVectorStoresAsync(sqlitePath);
            
           /* try
            {
                // stop any pending ingestion flags/tasks for current model
                if (Config is not null)
                {
                    _ingestionTasks.TryRemove(Config.Id, out _);
                    IngestionFinished = false;
                }

                if (File.Exists(sqlitePath))
                {
                    File.Delete(sqlitePath);
                    StatusMessage($"💣 Deleted vector DB file: {sqlitePath}");
                }
                else
                {
                    StatusMessage($"ℹ️ Vector DB file not found (already clean): {sqlitePath}");
                }

                // Recreate empty schema by touching collections
                await ForceCreateCollectionsAsync();
                StatusMessage("🧱 Vector collections recreated (empty).");
            }
            catch (Exception ex)
            {
                StatusMessage($"❌ Hard wipe failed: {ex.Message}");
            }*/
        }
        private async Task WipeCollectionsAsync()
        {
            var textChunks = _services.GetRequiredService<VectorStoreCollection<string, IngestedTextChunk>>();
            var sqlChunks = _services.GetRequiredService<VectorStoreCollection<string, IngestedSQLTableChunk>>();
            var codeChunks = _services.GetRequiredService<VectorStoreCollection<string, IngestedCodeChunk>>();
            var documents = _services.GetRequiredService<VectorStoreCollection<string, IngestedDocument>>();

            var allText = await textChunks.GetAsync(x => true, int.MaxValue).ToListAsync();
            if (allText.Any())
                await textChunks.DeleteAsync(allText.Select(x => x.Key));

            var allSql = await sqlChunks.GetAsync(x => true, int.MaxValue).ToListAsync();
            if (allSql.Any())
                await sqlChunks.DeleteAsync(allSql.Select(x => x.Key));

            var allCode = await codeChunks.GetAsync(x => true, int.MaxValue).ToListAsync();
            if (allCode.Any())
                await codeChunks.DeleteAsync(allCode.Select(x => x.Key));

            var allDocs = await documents.GetAsync(x => true, int.MaxValue).ToListAsync();
            if (allDocs.Any())
                await documents.DeleteAsync(allDocs.Select(x => x.Key));
        }

        public async Task SoftWipeVectorStoresAsync(string sqlitePath)
        {
            try
            {
                if (Config is not null)
                {
                    _ingestionTasks.TryRemove(Config.Id, out _);
                    IngestionFinished = false;
                }

                await WipeCollectionsAsync();
                StatusMessage("🧹 Vector store wiped (all collections emptied).");
            }
            catch (Exception ex)
            {
                StatusMessage($"❌ Wipe failed: {ex.Message}");
            }
        }

        public async Task RecreateAndReingestAsync()
        {
            var sqllitePath = _config.Current.VectorStoreFilePath;
            await HardWipeVectorStoresAsync(sqllitePath);

            try
            {
                // ensure DB registry reflects current config before ingest
                if (Config is not null && Config.Databases is not null)
                {
                    foreach (var dbCfg in Config.Databases)
                        _dbRegistry.Register(dbCfg);
                }

                OnStatusMessage?.Invoke("🚛 Re-ingesting all data…");
                await IngestDataAsync(Config);
                IngestionFinished = true;
                OnStatusMessage?.Invoke("✅ Re-ingest complete.");
            }
            catch (Exception ex)
            {
                StatusMessage($"❌ Re-ingest failed: {ex.Message}");
            }
        }
    }

}
