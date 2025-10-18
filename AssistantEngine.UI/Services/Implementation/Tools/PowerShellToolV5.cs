#if WINDOWS || WEB
// Only the changed parts from PowerShellTool to PowerShellToolV2

using AssistantEngine.Services.Implementation;
using AssistantEngine.Services.Implementation.Tools;
using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
namespace AssistantEngine.Services.Implementation.Tools
{
    public class PowerShellToolV5 : IDisposable
    {
        private readonly RunspacePool _runspacePool;
        private readonly IToolStatusNotifier _notifier;

        public PowerShellToolV5(IToolStatusNotifier notifier)
        {
            _notifier = notifier;

            // 1) Connect to the local Windows PowerShell endpoint via WS-Man
            var connInfo = new WSManConnectionInfo(
                new Uri("http://localhost:5985/wsman"),
                "http://schemas.microsoft.com/powershell/Microsoft.PowerShell",
                (PSCredential)null
            );
            connInfo.AuthenticationMechanism = AuthenticationMechanism.Default;

            // 2) Create the pool from the connection info
            _runspacePool = RunspaceFactory.CreateRunspacePool(1, 5, connInfo);
            _runspacePool.ThreadOptions = PSThreadOptions.UseNewThread;
            _runspacePool.Open();
        }

        public async Task<IEnumerable<string>> PowerShellScriptAsync(
            string script,
            IDictionary<string, object>? parameters = null
        )
        {
            _notifier.StatusMessage("Starting PowerShell script execution");

            using var ps = PowerShell.Create();
            ps.RunspacePool = _runspacePool;

            if (parameters != null)
            {
                var table = new Hashtable();
                foreach (var kv in parameters)
                    table[kv.Key] = kv.Value;

                ps.AddParameters(table);
            }

            ps.AddScript(script);
            var results = await Task.Run(() => ps.Invoke());

            if (ps.HadErrors)
            {
                var errors = string.Join("\n", ps.Streams.Error.Select(e => e.ToString()));
                _notifier.StatusMessage($"PowerShell script error: {errors}");
                throw new InvalidOperationException(errors);
            }

            _notifier.StatusMessage("PowerShell script completed successfully");
            return results.Select(r => r.ToString());
        }

        public void Dispose()
        {
            _runspacePool.Close();
            _runspacePool.Dispose();
        }
    }
}
#endif