using AssistantEngine.UI.Services.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssistantEngine.UI.Services.Extensions
{
    public static class StatusMessageExtensions
    {
        public static Action<string, StatusLevel> AsTwoArgs(this Action<string> oneArg)
            => (m, _) => oneArg(m);
    }
}
