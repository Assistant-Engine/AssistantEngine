using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssistantEngine.UI.Services
{
    public interface IModelInstaller
    {
        Task PullAsync(string model, CancellationToken ct = default);
        Task<bool> IsInstalledAsync(string model, CancellationToken ct = default);
    }
}
