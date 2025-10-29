using AssistantEngine.UI.Services.Implementation.Notifications;
using AssistantEngine.UI.Services.Models;
using AssistantEngine.UI.Services.Notifications;
using System.ComponentModel;

namespace AssistantEngine.Services.Implementation.Tools;

public sealed class ScheduleEvaluationTool : ITool
{
    private readonly IEvaluationStore _store; private readonly Func<AssistantConfig> _getConfig; private readonly IToolStatusNotifier _notifier;
    public ScheduleEvaluationTool(IEvaluationStore store, Func<AssistantConfig> getConfig, IToolStatusNotifier notifier)
    {
        _store = store;
        _getConfig = getConfig;
        _notifier = notifier;
    }
    private AssistantConfig CurrentConfig => _getConfig();

    [Description("Schedule a one-off evaluation. The model at ModelConfigId will later re-check this instruction and run tools if needed.")]
    public async Task<string> AddEvaluationAtAsync(
        [Description("ISO-8601 time (UTC or local).")] string whenIso,
        [Description("Text instruction describing the condition and what to do once the time has elapsed. Be Descriptive.")] string instruction)
    {
        var when = DateTimeOffset.Parse(whenIso, null, System.Globalization.DateTimeStyles.AssumeUniversal).ToUniversalTime();
        var e = new ScheduledEvaluation { ModelConfigId = CurrentConfig.Id, Instruction = instruction, DueUtc = when };
        var id = await _store.SaveAsync(e);
        _notifier.StatusMessage($"📝 Evaluation registered — {Trim(instruction, 80)}");
        return id.ToString();
    }
   
    [Description("Schedule a repeating evaluation every N seconds.")]
    public async Task<string> AddEvaluationRecurringAsync(
        [Description("Interval in seconds.")] int seconds,
        [Description("Text instruction describing the condition and what to do once the time has elapsed. Be Descriptive.")] string instruction)
    {
        var e = new ScheduledEvaluation
        {
            ModelConfigId = CurrentConfig.Id,
            Instruction = instruction,
            IntervalSeconds = seconds,
            Repeat = true,
            DueUtc = DateTimeOffset.UtcNow.AddSeconds(seconds)
        };
        var id = await _store.SaveAsync(e);
        _notifier.StatusMessage($"📝 Evaluation registered — {Trim(instruction, 80)}");
        return id.ToString();
    }

    [Description("Cancel a scheduled evaluation by Id.")]
    public Task<bool> CancelEvaluationAsync([Description("Evaluation Id (Guid).")] string id)
        => _store.CancelAsync(id);

    [Description("List scheduled evaluations as JSON.")]
    public async Task<string> ListEvaluationsAsync()
    {
        var all = await _store.ListAsync();
        return System.Text.Json.JsonSerializer.Serialize(all);
    }
    private static string Trim(string s, int n) => string.IsNullOrEmpty(s) ? s : (s.Length <= n ? s : s[..n] + "…");
}
