using AssistantEngine.Services.Extensions;
using AssistantEngine.Services.Implementation;
using AssistantEngine.Services.Implementation.Tools;
using AssistantEngine.UI.Pages.Chat;
using AssistantEngine.UI.Services;
using AssistantEngine.UI.Services.Extensions;
using AssistantEngine.UI.Services.Extensions;
using AssistantEngine.UI.Services.Implementation.Config;
using AssistantEngine.UI.Services.Implementation.Database;
using AssistantEngine.UI.Services.Implementation.Factories;
using AssistantEngine.UI.Services.Implementation.Ingestion;
using AssistantEngine.UI.Services.Implementation.MCP;
using AssistantEngine.UI.Services.Implementation.Models.Chat;
using AssistantEngine.UI.Services.Models;
using AssistantEngine.UI.Services.Models.Ingestion;
using AssistantEngine.UI.Services.Types;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;
using OllamaSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
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


    public class ChatClientState : IDisposable
    {
        public enum RunEngine { ChatClient, Agent }
        public RunEngine Engine { get; set; } = RunEngine.ChatClient;

        private readonly IAppConfigStore _config;
        private readonly IDatabaseRegistry _dbRegistry;
        readonly IToolStatusNotifier _notifier;
        readonly DataIngestor _dataIngestor;
        readonly ChatClientFactory _factory;
        readonly OllamaClientFactory _ollamaFactory;
        readonly IAssistantConfigStore _allConfigs;
        readonly IServiceProvider _services;
        private readonly IAppHealthService _health;
        private readonly IChatRepository _repo;
        private readonly AIAgentFactory _agentFactory;
        public ChatOptions ChatOptions { get; private set; }

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
             IAppHealthService health,
                 IChatRepository repo,
                   AIAgentFactory agentFactory)
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
            _repo = repo;
            _agentFactory = agentFactory;

            _notifier.OnStatusMessage -= StatusMessage;
            _notifier.OnStatusMessage += StatusMessage;

            _dbRegistry.DatabaseAdded -= OnDatabaseAddedAsync;
            _dbRegistry.DatabaseAdded += OnDatabaseAddedAsync;

            _dbRegistry.DatabaseRemoved -= OnDatabaseRemoved;
            _dbRegistry.DatabaseRemoved += OnDatabaseRemoved;
        }


        private CancellationTokenSource? _runCts;
        private ChatMessage? _inflight;
        private DateTime _lastFlush = DateTime.MinValue;
        private static readonly TimeSpan FlushThrottle = TimeSpan.FromMilliseconds(80);
        private static readonly TimeSpan UiInterval = TimeSpan.FromMilliseconds(50); // render cadence
        private DateTime _lastUi = DateTime.MinValue;
        private static readonly TimeSpan UiRenderPause = TimeSpan.FromMilliseconds(2);
        public event Action? OnStateChanged;
        public ChatSession Session { get; private set; } = new();
        public List<ChatMessage> Messages { get; } = new();
        public bool IsLoading { get; private set; }
        public bool IsStreaming { get; private set; }
        public ChatMessage? InProgress => _inflight;
        public event Action<bool, string?>? OnLoaderMessage;
        public event Action<bool, string?>? OnChatLoaderMessage;
        public event Action<string>? OnStatusEvent;

        public ChatResponse? ChatResponse { get; private set; }
        readonly ConcurrentDictionary<string, Task> _ingestionTasks = new();
        public IChatClient Client { get; set; }
        public IOllamaApiClient OllamaClient { get; private set; }

        public AIAgent AiAgent { get; private set; }
        public AssistantConfig Config { get; private set; }
        public bool IngestionFinished { get; private set; } = false;
        public StatusLevel DefaultStatusSeverity { get; set; } = StatusLevel.Information;
        private void RaiseStateChanged() => OnStateChanged?.Invoke();

        private async Task RaiseStateChangedThrottledAsync(CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;

            if ((now - _lastUi) >= UiInterval)
            {
                _lastUi = now;
                OnStateChanged?.Invoke();
                await Task.Delay(UiRenderPause, ct); 
            }
        }

        private async void OnDatabaseAddedAsync(IDatabase db)
        {
            await IngestNewDatabaseAsync(db);
        }

        private void OnDatabaseRemoved(string id)
        {
            StatusMessage($"Database {id} removed – its chunks will no longer be available.", StatusLevel.Information);
        }
        private async Task IngestNewDatabaseAsync(IDatabase db)
        {
            try
            {
                var descriptorClient = _factory(Config.DescriptorModel.ModelId);
                var sqlSource = new DatabaseIngestionSource(db, _factory, Config);
                sqlSource.OnProgressMessage += LoaderMessage;
                await _dataIngestor.IngestDataAsync(
                    sqlSource,
                    "sql-table-chunks",
                    "data-echoed-documents"
                );

                StatusMessage($"Database {db.Configuration.Id} ingested.", StatusLevel.Information);
            }
            catch (Exception ex)
            {
                StatusMessage($"Error ingesting {db.Configuration.Id}: {ex.Message}", StatusLevel.Information);
            }
        }


        public void StatusMessage(StatusMessage s) => OnStatusMessage?.Invoke(s);

        public void StatusMessage(StatusMessage msg, StatusLevel minSeverity) // filtered
        {
            if (msg.Level >= minSeverity) OnStatusMessage?.Invoke(msg);
        }
        public void StatusMessage(string msg, StatusLevel level = StatusLevel.Information,
                                  string? title = null, string? source = null, StatusLevel minSeverity = StatusLevel.Information)
            => StatusMessage(new StatusMessage(msg, level, title, source), minSeverity);

        public event Action<StatusMessage>? OnStatusMessage;

        
        private void LoaderMessage(string msg) => OnLoaderMessage?.Invoke(true,msg);
        private void ChatLoaderMessage(string msg) => OnChatLoaderMessage?.Invoke(true,msg);

   

        public async Task LoadSessionAsync(ChatSession? s = null, CancellationToken ct = default)
        {
            Session = s ?? new ChatSession();
            Messages.Clear();

            if (Session.Messages?.Count > 0)
                Messages.AddRange(Session.Messages);
            else
                Messages.Add(new(ChatRole.System, Config.SystemPrompt));

            RaiseStateChanged();
            await Task.CompletedTask;
        }

        public async Task ResetConversationAsync()
        {
            _runCts?.Cancel();
            Session = new ChatSession();
            Messages.Clear();
            Messages.Add(new(ChatRole.System, Config.SystemPrompt));
            await _repo.SaveAsync(Session);
            RaiseStateChanged();
        }

        private List<ChatMessage> Filter(List<ChatMessage> src)
        {
            var list = src.ToList();
            if (!Config.PersistThoughtHistory)
                list.RemoveThinkMessages();
            return list;
        }

        public async Task EnqueueAndRunAsync(ChatMessage userMsg)
        {
            var o = _health.Get(HealthDomain.Ollama);
            if (o.Level != HealthLevel.Healthy)
            {
                StatusMessage(o.Error ?? "Ollama not reachable.", StatusLevel.Error);
                return;
            }

            _runCts?.Cancel();
            Messages.Add(userMsg);
            IsLoading = true;
           // RaiseStateChanged();

            await StartRunAsync(Filter(Messages));
        }
        public async Task EditAndRegenerateAsync(string messageId, string newText)
        {
            _runCts?.Cancel();

            var idx = Messages.FindIndex(m => m.MessageId == messageId);
            if (idx < 0) return;

            Messages[idx] = new(ChatRole.User, newText);
            if (idx < Messages.Count - 1)
                Messages.RemoveRange(idx + 1, Messages.Count - (idx + 1));

            IsLoading = true;
            RaiseStateChanged();

            await StartRunAsync(Filter(Messages));
        }
        public void CancelRun()
        {
            _runCts?.Cancel();
        }

  
        private async Task FlushAsync(bool force = false, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            if (!force && (now - _lastFlush) < FlushThrottle) return;

            Session.Messages = Messages;
            await _repo.SaveAsync(Session, ct);
            _lastFlush = now;
        }
   /// <summary>
   /// method for external libraries maintaining chat state
   /// </summary>
   /// <param name="messages"></param>
   /// <param name="chatOptions"></param>
   /// <param name="cancellationToken"></param>
   /// <param name="newStream">whether to use new messages or not</param>
   /// <returns></returns>
        public async Task<ChatResponse> GetChatResponseAsync(List<ChatMessage> messages,ChatOptions chatOptions = null,CancellationToken cancellationToken = default, bool newStream = true, bool setTitle = false)
        {
            if(newStream)
            {
                Messages.Clear();
            }
            Messages.AddRange(messages);
            var chatOpsToUse = chatOptions == null ? ChatOptions : chatOptions;
       
            var start = DateTime.UtcNow;
            var mergedAdditional = new Microsoft.Extensions.AI.AdditionalPropertiesDictionary();

            // will throw for cancellation / failures to caller
            var chatResponse = await Client.GetResponseAsync(messages, chatOpsToUse, cancellationToken).ConfigureAwait(false);
             
            if (chatResponse?.AdditionalProperties != null)
                foreach (var kv in chatResponse.AdditionalProperties)
                    mergedAdditional[kv.Key] = kv.Value;

            chatResponse.AdditionalProperties ??= new();
            foreach (var kv in mergedAdditional)
                chatResponse.AdditionalProperties[kv.Key] = kv.Value;
            var finish = DateTime.UtcNow;
            var totalAll = finish - start;
            ChatResponse = chatResponse;
            Messages.AddMessages(chatResponse);
            await FlushAsync(force: true);

            if (Session.DefaultTitle() && setTitle)
                _ = SetTitleAsync();
            return chatResponse;
        }

        public async Task<ChatResponse> GetChatResponseAsync(ChatMessage userMsg)
        {
            var o = _health.Get(HealthDomain.Ollama);
            if (o.Level != HealthLevel.Healthy)
            {
                StatusMessage(o.Error ?? "Ollama not reachable.", StatusLevel.Error);
                return null;
            }

            _runCts?.Cancel();
            Messages.Add(userMsg);
            IsLoading = true;
            return await GetChatResponseAsync(Filter(Messages));
        }
        public async Task StartAgentRunAsync()
        {
            _runCts?.Dispose();
            _runCts = new CancellationTokenSource();
            var token = _runCts.Token;

            ChatResponse = null;
            IsStreaming = true;
            IsLoading = true;
            _inflight = null;
            RaiseStateChanged();
            await Task.Yield();
            _ = FlushAsync();

            var thread = AiAgent.GetNewThread();

            _inflight = new ChatMessage
            {
                MessageId = Guid.NewGuid().ToString(),
                Role = ChatRole.Assistant,
                CreatedAt = DateTime.UtcNow,
                AdditionalProperties = new()
            };

            try
            {
                var start = DateTime.UtcNow;
                DateTime? firstToken = null;

                var updatesBuffer = new List<Microsoft.Agents.AI.AgentRunResponseUpdate>();
                var mergedAdditional = new Microsoft.Extensions.AI.AdditionalPropertiesDictionary();

                await foreach (var update in AiAgent.RunStreamingAsync(thread, cancellationToken: token).ConfigureAwait(false))
                {
                    if (update == null) continue;

                    updatesBuffer.Add(update);

                    if (update.AdditionalProperties != null)
                        foreach (var kv in update.AdditionalProperties)
                            mergedAdditional[kv.Key] = kv.Value;

                    foreach (var c in update.Contents ?? [])
                    {
                        if (!firstToken.HasValue)
                        {
                            firstToken = DateTime.UtcNow;
                            _inflight.AdditionalProperties["StartedThinkingAt"] = DateTime.UtcNow;
                        }

                        var last = _inflight.Contents.LastOrDefault();

                        if (c is TextReasoningContent r && last is TextReasoningContent lr)
                        {
                            lr.Text += r.Text;
                            MergeProps(lr, r);
                        }
                        else if (c is TextContent t && last is TextContent lt)
                        {
                            if (!_inflight.AdditionalProperties.ContainsKey("FinishedThinkingAt"))
                                _inflight.AdditionalProperties["FinishedThinkingAt"] = DateTime.UtcNow;

                            lt.Text += t.Text;
                            MergeProps(lt, t);
                        }
                        else
                        {
                            if (last?.AdditionalProperties != null)
                                last.AdditionalProperties["FinishedAt"] = DateTime.UtcNow;

                            c.AdditionalProperties ??= new();
                            c.AdditionalProperties["StartedAt"] = DateTime.UtcNow;
                            _inflight.Contents.Add(c);
                        }
                    }

                    await RaiseStateChangedThrottledAsync(token);
                }

                async IAsyncEnumerable<AgentRunResponseUpdate> Replay()
                { foreach (var u in updatesBuffer) yield return u; }

                var agentRunResponse = await Replay().ToAgentRunResponseAsync(token);
                agentRunResponse.AdditionalProperties ??= new();
                foreach (var kv in mergedAdditional)
                    agentRunResponse.AdditionalProperties[kv.Key] = kv.Value;

                var finish = DateTime.UtcNow;
                var totalAll = finish - start;
                if (!agentRunResponse.AdditionalProperties.ContainsKey("total_duration_all"))
                    agentRunResponse.AdditionalProperties["total_duration_all"] = totalAll;

                var load = firstToken.HasValue ? (firstToken.Value - start) : totalAll;
                if (!agentRunResponse.AdditionalProperties.ContainsKey("load_duration"))
                    agentRunResponse.AdditionalProperties["load_duration"] = load;

                var eval = firstToken.HasValue ? (finish - firstToken.Value) : TimeSpan.Zero;
                if (!agentRunResponse.AdditionalProperties.ContainsKey("eval_duration"))
                    agentRunResponse.AdditionalProperties["eval_duration"] = eval;
                if (!agentRunResponse.AdditionalProperties.ContainsKey("total_duration"))
                    agentRunResponse.AdditionalProperties["total_duration"] = eval;
                if (!agentRunResponse.AdditionalProperties.ContainsKey("tool_duration"))
                    agentRunResponse.AdditionalProperties["tool_duration"] = TimeSpan.Zero;

                Messages.Add(_inflight);
                Session.Messages = Messages;
                
                _inflight = null;

                ChatResponse = agentRunResponse.AsChatResponse();
                IsStreaming = false;
                IsLoading = false;

                await FlushAsync(force: true);

                if (Session.DefaultTitle())
                    _ = SetTitleAsync();

                RaiseStateChanged();
            }
            catch (OperationCanceledException)
            {
                IsStreaming = false;
                IsLoading = false;
                if (_inflight != null)
                {
                    Messages.Add(_inflight);
                    _inflight = null;
                    Session.Messages = Messages;
                    await FlushAsync(force: true);
                }
                RaiseStateChanged();
            }
            catch (Exception ex)
            {
                IsStreaming = false;
                IsLoading = false;
                StatusMessage($"Agent streaming error: {ex.Message}", StatusLevel.Error);
                RaiseStateChanged();
            }
        }

        public async Task StartRunAsync(List<ChatMessage> filtered)
        {
            _runCts?.Dispose();
            _runCts = new CancellationTokenSource();
            var token = _runCts.Token;

            ChatResponse = null;
            IsStreaming = true;
            IsLoading = true;
            _inflight = null;
            RaiseStateChanged();
            await Task.Yield(); 
            _ = FlushAsync();

            _inflight = new ChatMessage
            {
                MessageId = Guid.NewGuid().ToString(),
                Role = ChatRole.Assistant,
                CreatedAt = DateTime.UtcNow,
                AdditionalProperties = new()
            };
            try
            {
                var start = DateTime.UtcNow;
                DateTime? firstToken = null;

                var updatesBuffer = new List<ChatResponseUpdate>();
                var mergedAdditional = new Microsoft.Extensions.AI.AdditionalPropertiesDictionary();

                await foreach (var update in Client.GetStreamingResponseAsync(filtered, ChatOptions, token).ConfigureAwait(false))
                {
                    updatesBuffer.Add(update);
                    if (update?.AdditionalProperties != null)
                        foreach (var kv in update.AdditionalProperties)
                            mergedAdditional[kv.Key] = kv.Value;

                    foreach (var c in update?.Contents ?? [])
                    {
                        if (!firstToken.HasValue)
                        {
                            firstToken = DateTime.UtcNow;
                            _inflight.AdditionalProperties["StartedThinkingAt"] = DateTime.UtcNow;
                        }

                        var last = _inflight.Contents.LastOrDefault();

                        if (c is TextReasoningContent r && last is TextReasoningContent lr)
                        {
                            lr.Text += r.Text;
                            MergeProps(lr, r);
                        }
                        else if (c is TextContent t && last is TextContent lt)
                        {
                            if (!_inflight.AdditionalProperties.ContainsKey("FinishedThinkingAt"))
                                _inflight.AdditionalProperties["FinishedThinkingAt"] = DateTime.UtcNow;

                            lt.Text += t.Text;
                            MergeProps(lt, t);
                        }
                        else
                        {
                            if (last?.AdditionalProperties != null)
                                last.AdditionalProperties["FinishedAt"] = DateTime.UtcNow;

                            c.AdditionalProperties ??= new();
                            c.AdditionalProperties["StartedAt"] = DateTime.UtcNow;
                            _inflight.Contents.Add(c);
                        }//could flush more here for persistence
                    }
                    // RequestRenderThrottled();

                    // await RaiseStateChangedThrottledTimedAsync();
                    await RaiseStateChangedThrottledAsync(token); 
                }

                async IAsyncEnumerable<ChatResponseUpdate> Replay()
                { foreach (var u in updatesBuffer) yield return u; }

                var chatResponse = await Replay().ToChatResponseAsync(token);
                chatResponse.AdditionalProperties ??= new();
                foreach (var kv in mergedAdditional)
                    chatResponse.AdditionalProperties[kv.Key] = kv.Value;

                var finish = DateTime.UtcNow;
                var totalAll = finish - start;
                if (!chatResponse.AdditionalProperties.ContainsKey("total_duration_all"))
                    chatResponse.AdditionalProperties["total_duration_all"] = totalAll;

                var load = firstToken.HasValue ? (firstToken.Value - start) : totalAll;
                if (!chatResponse.AdditionalProperties.ContainsKey("load_duration"))
                    chatResponse.AdditionalProperties["load_duration"] = load;

                var eval = firstToken.HasValue ? (finish - firstToken.Value) : TimeSpan.Zero;
                if (!chatResponse.AdditionalProperties.ContainsKey("eval_duration"))
                    chatResponse.AdditionalProperties["eval_duration"] = eval;
                if (!chatResponse.AdditionalProperties.ContainsKey("total_duration"))
                    chatResponse.AdditionalProperties["total_duration"] = eval;
                if (!chatResponse.AdditionalProperties.ContainsKey("tool_duration"))
                    chatResponse.AdditionalProperties["tool_duration"] = TimeSpan.Zero;

                Messages.Add(_inflight);
                Session.Messages = Messages;
                _inflight = null;

                ChatResponse = chatResponse;
                IsStreaming = false;
                IsLoading = false;

                await FlushAsync(force: true);

                if (Session.DefaultTitle())
                    _ = SetTitleAsync();

                RaiseStateChanged();
            }
            catch (OperationCanceledException)
            {
                IsStreaming = false;
                IsLoading = false;
                if (_inflight != null)
                {
                    Messages.Add(_inflight);
                    _inflight = null;
                    Session.Messages = Messages;
                    await FlushAsync(force: true);
                }
                RaiseStateChanged();
            }
            catch (Exception ex)
            {
                IsStreaming = false;
                IsLoading = false;
                StatusMessage($"Streaming error: {ex.Message}", StatusLevel.Error);
                RaiseStateChanged();
            }
        }

        private static void MergeProps(AIContent target, AIContent src)
        {
            if (src.AdditionalProperties is null) return;
            target.AdditionalProperties ??= new();
            foreach (var kv in src.AdditionalProperties) target.AdditionalProperties[kv.Key] = kv.Value;
        }

        private async Task SetTitleAsync()
        {
            try
            {
                var miniOptions = Config.ModelOptions.First(m => m.Key == "MiniTask").Options;
                var msgs = new List<ChatMessage>
        {
            new(ChatRole.System, "Output a maximum three word title for text you receive. Only ever output these words."),
            new(ChatRole.User, Messages.First(x => x.Role == ChatRole.User).Text)
        };
                var resp = await _factory(Config.MiniTaskModel.ModelId).GetResponseAsync(msgs, miniOptions);
                var title = AssistantEngine.Services.Extensions.ChatMessageExtensions.RemoveThinkTags(
                    resp.Messages.First(x => x.Role == ChatRole.Assistant).Text);
                Session.Title = title;
                await _repo.SaveAsync(Session);
                RaiseStateChanged();
            }
            catch { /* non-fatal */ }
        }



        public Task ChangeModelAsync(string id = "") =>ChangeModelAsync(id, DefaultStatusSeverity);
        public async Task ChangeModelAsync(string id, StatusLevel minStatusSeverity)
        {
            if (false/*!_health.Snapshot.OllamaConnected || _health.Snapshot.OllamaError != null*/)
            {
               // OnStatusMessage?.
               // (_health.Snapshot.OllamaError ?? "Ollama not reachable. Model features disabled.");// either here DEFINITELY HERE
                //return;
            }
            if (string.IsNullOrEmpty(id))
            {
                id = _allConfigs.GetAll().FirstOrDefault(c => c.Default)?.Id ?? _allConfigs.GetAll().First().Id;
            }
            Config = _allConfigs.GetAll().First(c => c.Id == id); // not set to an instance of an object once
            Client = _factory(Config.AssistantModel.ModelId);
            OllamaClient = _ollamaFactory(Config.AssistantModel.ModelId);
            AiAgent = _agentFactory(Config.AssistantModel.ModelId);
            foreach (var dbConfig in Config.Databases)
            {
                _dbRegistry.Register(dbConfig);
            }

            var mcp = _services.GetRequiredService<IMcpRegistry>();
            foreach (var conn in Config.McpConnectors)
            {
                // fire and forget connect
                _ = mcp.RegisterAsync(conn).ContinueWith(t =>
                {
                    if (t.Exception != null)
                    {
                        StatusMessage($"MCP '{conn.Id}' failed: {t.Exception.InnerException?.Message}", StatusLevel.Error, minSeverity: minStatusSeverity);
                    }
                    else
                    {
                        StatusMessage($"MCP '{conn.Id}' connected.", StatusLevel.Success,minSeverity:minStatusSeverity);
                    }
                });
            }

            var ingestTask = _ingestionTasks.GetOrAdd(id, _ => IngestDataAsync(Config));
                await ingestTask;
                IngestionFinished = true;
            
    
            StatusMessage($"Model “{Config.Name}” ready", StatusLevel.Information, minSeverity: minStatusSeverity);
            ChatOptions = Config.WithEnabledToolsAndMcp(_services);

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
                    StatusMessage($"Database {item.Id} not found in registry.", StatusLevel.Error);
                    continue;
                }

                try
                {
                    var dbSource = new DatabaseIngestionSource(db, _factory, Config);
                    dbSource.OnProgressMessage += LoaderMessage;

                    await _dataIngestor.DeleteSourceAsync(
                        "data-echoed-documents", "sql-table-chunks", dbSource.SourceId);
                }
                catch (Exception ex)
                {
                    StatusMessage($"Error reingesting database {item.Id}: {ex.Message}", StatusLevel.Error);
                }
            }
            await IngestDataAsync(Config);
        }

        public async Task ReingestDatabase(DatabaseConfiguration item)
        {
            var db = _dbRegistry.Get(item.Id);
            if (db == null)
            {
                StatusMessage($"Database {item.Id} not found in registry.", StatusLevel.Error);
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
                sqlSource.OnProgressMessage += LoaderMessage;

                await _dataIngestor.IngestDataAsync(
                    sqlSource,
                    "sql-table-chunks",
                    "data-echoed-documents"
                );
            }
            catch (Exception ex)
            {
                StatusMessage($"Error reingesting database {item.Id}: {ex.Message}", StatusLevel.Error);
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
                    StatusMessage($"Path '{item.Path}' does not exist – skipping.", StatusLevel.Error);
                    continue;
                }

                try
                {

                    var csSource = new CSDirectorySource(item.Path, item.ExploreSubFolders);
                    csSource.OnProgressMessage += LoaderMessage;

                    var pdfSource = new PDFDirectorySource(item.Path, item.ExploreSubFolders);
                    pdfSource.OnProgressMessage += LoaderMessage;

                    var generalSource = new GeneralDirectorySource(item.Path, item.ExploreSubFolders, item.FileExtensions);
                    generalSource.OnProgressMessage += LoaderMessage;

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
                    StatusMessage($"Error reingesting documents from {item.Path}: {ex.Message}", StatusLevel.Error);
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
                StatusMessage(o.Error ?? "Ollama not reachable; skipping ingestion.", StatusLevel.Error);
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
                    csSource.OnProgressMessage += LoaderMessage;
                    await _dataIngestor.IngestDataAsync(csSource, "code-chunks", "data-echoed-documents");

                   /* var codeChunksCount = await _dataIngestor.CountChunksAsync("code-chunks");
                    var csDocsCount = await _dataIngestor.CountDocumentsAsync("data-echoed-documents", csSource.SourceId);
                    LoaderMessage($"code-chunks total: {codeChunksCount}");
                    LoaderMessage($"docs for {csSource.SourceId}: {csDocsCount}");*/


                    var generalSource = new GeneralDirectorySource(item.Path, item.ExploreSubFolders, item.FileExtensions);
                    generalSource.OnProgressMessage += LoaderMessage;
                    await _dataIngestor.IngestDataAsync(generalSource, "text-chunks", "data-echoed-documents");

                    /*var textChunksCountGeneral = await _dataIngestor.CountChunksAsync("text-chunks");
                    var generalDocsCount = await _dataIngestor.CountDocumentsAsync("data-echoed-documents", generalSource.SourceId);
                    LoaderMessage($"text-chunks total: {textChunksCountGeneral}");
                    LoaderMessage($"docs for {generalSource.SourceId}: {generalDocsCount}");*/


                    var pdfSource = new PDFDirectorySource(item.Path, item.ExploreSubFolders);
                    pdfSource.OnProgressMessage += LoaderMessage;
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
                    sqlSource.OnProgressMessage +=  LoaderMessage;
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
        public void Dispose()
        {
            _notifier.OnStatusMessage -= StatusMessage;
            _dbRegistry.DatabaseAdded -= OnDatabaseAddedAsync;
            _dbRegistry.DatabaseRemoved -= OnDatabaseRemoved;
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
                StatusMessage("🧹 Vector store wiped (all collections emptied).", StatusLevel.Information);
            }
            catch (Exception ex)
            {
                StatusMessage($"❌ Wipe failed: {ex.Message}", StatusLevel.Error);
            }
        }

        public async Task RecreateAndReingestAsync()
        {
            var sqllitePath = _config.Current.VectorStoreFilePath;
            await HardWipeVectorStoresAsync(sqllitePath);

            try
            {
                if (Config is not null && Config.Databases is not null)
                {
                    foreach (var dbCfg in Config.Databases)
                        _dbRegistry.Register(dbCfg);
                }

                StatusMessage("🚛 Re-ingesting all data…", StatusLevel.Information);
                await IngestDataAsync(Config);
                IngestionFinished = true;
                StatusMessage("✅ Re-ingest complete.", StatusLevel.Success);
            }
            catch (Exception ex)
            {
                StatusMessage($"❌ Re-ingest failed: {ex.Message}", StatusLevel.Error);
            }
        }
    }

}
