using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssistantEngine.UI.Services.Options
{
    public sealed class ConfigStorageOptions
    {
        /// <summary>Absolute path to the JSON file (e.g., .../App_Data/appsettings.AssistantEngine.json)</summary>
        public string ConfigFilePath { get; init; } = default!;

        /// <summary>Default data root; differs for Web vs App.</summary>
        public string DefaultDataRoot { get; init; } = default!;
    }
}
