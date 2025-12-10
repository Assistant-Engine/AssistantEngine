using AssistantEngine.Services.Extensions;
using AssistantEngine.Services.Implementation;
using AssistantEngine.UI.Services.Extensions;
using AssistantEngine.UI.Services.Implementation.Config;
using AssistantEngine.UI.Services.Implementation.Factories;
using AssistantEngine.UI.Services.Implementation.Notifications;
using AssistantEngine.UI.Services.Implementation.Ollama;
using AssistantEngine.UI.Services.Models;
using AssistantEngine.UI.Services.Notifications;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.ComponentModel;
using System.Reflection;


namespace AssistantEngine.UI.Services.Notifications;
// so a couple of things about this 
//should we have a page where we can view Evaluations seperately for debugging etc or is it not worth it.
// 1. What about when the condition hasnt passed but it is a recurring task? background evaluator should be setup with clear response options
// it might need to do a tool to see if it has passed. 
// I think it needs to use ToolStatusNotifier Or a seperate notifier
public sealed class EvaluationSchedulerService : BackgroundService
{// add near top of the file
    private enum EvaluationResult { Pass, Defer, Error }
    private const int DefaultDeferSeconds = 30;

    private readonly IEvaluationStore _store;
    private readonly IAssistantConfigStore _configs;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IToolStatusNotifier _notifier;
    public EvaluationSchedulerService(
        IEvaluationStore store,
        IAssistantConfigStore configs,
        IServiceScopeFactory scopeFactory,
            IToolStatusNotifier notifier)
    {
        _store = store;
        _configs = configs;
        _scopeFactory = scopeFactory;
        _notifier = notifier;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;

            await foreach (var eval in _store.DueAsync(now, stoppingToken))
            {
                // Skip disabled or paused evaluations
                if (eval.State == EvalState.Disabled || eval.State == EvalState.Paused)
                    continue;

                if (eval.ExpiresUtc is { } exp && now > exp)
                {
                    eval.State = EvalState.Completed;
                    await _store.UpdateAsync(eval, stoppingToken);
                    continue;
                }
                var outcome = await RunModelEvaluationAsync(eval, stoppingToken);
                eval.LastCheckUtc = now;

                switch (outcome)
                {
                    case EvaluationResult.Pass:
                        if (eval.IntervalSeconds is int s && eval.Repeat)
                        {
                            eval.NextCheckUtc = now.AddSeconds(s); eval.State = EvalState.Pending;
                        }
                        else { eval.State = EvalState.Fired; eval.NextCheckUtc = null; }
                        break;

                    case EvaluationResult.Defer:
                        // optional: keep quiet or inform
                        //_notifier.StatusMessage($"⏳ Evaluation deferred: {eval.Id} — checking again soon");
                        if (eval.IntervalSeconds is int rs && eval.Repeat)
                        {
                            eval.NextCheckUtc = now.AddSeconds(rs); eval.State = EvalState.Pending;
                        }
                        else { eval.NextCheckUtc = now.AddSeconds(DefaultDeferSeconds); eval.State = EvalState.Pending; }
                        break;

                    default: // Error
                        _notifier.StatusMessage($"❌ Evaluation error: {eval.Id}", Types.StatusLevel.Error);
                        eval.NextCheckUtc = now.AddSeconds(DefaultDeferSeconds);
                        eval.State = EvalState.Pending;
                        break;
                }
                await _store.UpdateAsync(eval, stoppingToken);

            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }


    private static (string level, string title, string message)? TryParseNotify(string fullText)
    {
        var line = fullText.Split('\n').FirstOrDefault(l =>
            l.TrimStart().StartsWith("NOTIFY:", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(line)) return null;

        var json = line[(line.IndexOf(':') + 1)..].Trim();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var lvl = root.TryGetProperty("level", out var l) ? l.GetString() ?? "info" : "info";
            var ttl = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            var msg = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            return (lvl, ttl, msg);
        }
        catch { return null; }
    }

    //i think we need to change this to a json model. so this is clearer the communication
    private async Task<EvaluationResult> RunModelEvaluationAsync(ScheduledEvaluation eval, CancellationToken ct)
    {
        EvaluationResult result = EvaluationResult.Error;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var services = scope.ServiceProvider;

            var cfg = _configs.GetById(eval.ModelConfigId);
            var resolver = services.GetRequiredService<IOllamaClientResolver>();
            var clientUnresolved = (IChatClient)resolver.For(cfg, cfg.AssistantModel.ModelId);
            var client = clientUnresolved.AsBuilder().UseFunctionInvocation().Build();
            // Tools from the same scoped provider
            var options = cfg.WithEnabledTools(services);

            // STRICT protocol so the scheduler can act deterministically:
            var system = new ChatMessage(ChatRole.System, """
            You run in the background and return evaluation notifications to the user.
            You must follow the instruction. Dont do anything else.
            The notify object is used to send the notification upon pass.
            You may CALL TOOLS to check conditions or to complete the user task if neccessary.
            Never use an AddEvaluation tool.


            Return **only JSON**, with no extra text, matching this schema:
            {
              "result": "pass | defer | error",
              "state": { ...optional... },
              "notify": {
                "level": "info | success | warning | error",
                "title": "string",
                "message": "string"
              }
            }

            Rules:
            - If condition passed and required actions/tools executed → result = "pass"
            - If not yet → result = "defer"
            - On unrecoverable failure → result = "error" (include error message)
            - Keep JSON minimal — omit unnecessary properties
            """);


            var user = new ChatMessage(ChatRole.User,
                $"NowUtc: {DateTimeOffset.UtcNow:O}\n" +
                $"Instruction:\n{eval.Instruction}\n" +
                $"PreviousStateJson:\n{eval.ScratchpadJson ?? "{}"}");

            var resp = await client.GetResponseAsync(new List<ChatMessage> { system, user }, options, ct);

            var text = resp.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant)?.Text ?? string.Empty;

            // Try to parse JSON (assistant must output JSON only per system prompt)
            string json = "";
          
            try
            {
                var desanText = ChatMessageExtensions.RemoveThinkTags(text);
                // If the model accidentally wraps JSON with code fences or whitespace, extract inner JSON
                json = ExtractJson(desanText);  //error from here // find where we put remove think tags

                var reply = System.Text.Json.JsonSerializer.Deserialize<EvaluationResponse>(json);

                if (reply?.Result is null)
                {
                    _notifier.StatusMessage("TOAST|warning|Evaluator|No 'result' in JSON.");
                    return EvaluationResult.Error;
                }

                switch (reply.Result.ToLowerInvariant())
                {
                    case "pass": result = EvaluationResult.Pass; break;
                    case "defer": result = EvaluationResult.Defer; break;
                    case "error": result = EvaluationResult.Error; break;
                    default:
                        _notifier.StatusMessage($"TOAST|warning|Evaluator|Unknown result '{reply.Result}'.");
                        result = EvaluationResult.Error;
                        break;
                }

                // Persist state if provided
                if (reply.State is System.Text.Json.JsonElement s)
                    eval.ScratchpadJson = s.GetRawText();

                // On PASS, forward notify payload (if any) to toastr
                if (result == EvaluationResult.Pass && reply.Notify is not null)
                {
                    var lvl = string.IsNullOrWhiteSpace(reply.Notify.Level) ? "success" : reply.Notify.Level!;
                    var ttl = string.IsNullOrWhiteSpace(reply.Notify.Title) ? "Evaluation" : reply.Notify.Title!;
                    var msg = reply.Notify.Message ?? "";
                    _notifier.StatusMessage($"TOAST|{lvl}|{ttl}|{msg}");
                }

                // On ERROR with detail
                if (result == EvaluationResult.Error && !string.IsNullOrWhiteSpace(reply.Error))
                    _notifier.StatusMessage($"TOAST|error|Evaluator error|{reply.Error}");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                _notifier.StatusMessage($"TOAST|error|Evaluator JSON parse|{ex.Message}");
                result = EvaluationResult.Error;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.StackTrace);
            _notifier.StatusMessage($"TOAST|error|Evaluator JSON parse|{ex.Message}");
            result = EvaluationResult.Error;
        }

        return result;
    }
    static string ExtractJson(string text)
    {
        // Fast path: trim and check first/last braces
        var t = text.Trim();
        if (t.StartsWith("{") && t.EndsWith("}")) return t;

        // Try to pull JSON from common wrappers (```json … ```)
        var start = t.IndexOf('{');
        var end = t.LastIndexOf('}');
        if (start >= 0 && end > start) return t.Substring(start, end - start + 1);

        // Last resort: return original (will throw, caught by caller)
        return t;
    }

    private static string Trim(string s, int n) => string.IsNullOrEmpty(s) ? s : (s.Length <= n ? s : s[..n] + "…");

}
