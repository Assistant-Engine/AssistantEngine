using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssistantEngine.UI.Services.Implementation.Notifications
{


    public enum EvalState { Pending, Fired, Paused, Disabled, Completed }

    public sealed class ScheduledEvaluation
    {
        public Guid Id { get; init; } = Guid.NewGuid();

        // Text instruction the model will evaluate (and possibly call tools from)
        public string Instruction { get; init; } = "";

        // Which AssistantConfig to use
        public string ModelConfigId { get; init; } = "";

        // Scheduling
        public DateTimeOffset? DueUtc { get; init; }            // one-off
        public int? IntervalSeconds { get; init; }              // repeat
        public bool Repeat { get; init; } = false;

        // Cursor/state
        public EvalState State { get; set; } = EvalState.Pending;
        public DateTimeOffset? NextCheckUtc { get; set; }
        public DateTimeOffset? LastCheckUtc { get; set; }

        // Model-visible state (you can store anything you want to feed back)
        public string? ScratchpadJson { get; set; } = null;

        // Optional retention
        public DateTimeOffset? ExpiresUtc { get; init; }
    }
}
