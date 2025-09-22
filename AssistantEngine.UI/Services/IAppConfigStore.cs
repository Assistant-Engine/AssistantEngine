using AssistantEngine.UI.Services.Implementation.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssistantEngine.UI.Services
{
    public interface IAppConfigStore
    {
        AppConfig Current { get; }
        string ConfigFilePath { get; } // add this
        string AppDataDirectory { get; }   // <--- new
        Task SaveAsync(AppConfig config, CancellationToken ct = default);
        Task ReloadAsync(CancellationToken ct = default);      // read from disk (if present) else defaults
        Task ResetToDefaultsAsync(CancellationToken ct = default); // regenerate defaults and overwrite file
        Task UnloadAsync(CancellationToken ct = default);      // forget current & reload defaults w/o file

        event EventHandler<AppConfig>? Changed;
    }
}
