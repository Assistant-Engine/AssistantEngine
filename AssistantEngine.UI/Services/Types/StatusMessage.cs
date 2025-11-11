using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssistantEngine.UI.Services.Types
{
    public sealed record StatusMessage(
        string Message,
        StatusLevel Level = StatusLevel.Information,
        string? Title = null,
        string? Source = null,
        DateTimeOffset? Timestamp = null
    );
}
