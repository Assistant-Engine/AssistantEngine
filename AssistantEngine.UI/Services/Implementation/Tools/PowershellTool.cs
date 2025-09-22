using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.Threading.Tasks;

namespace AssistantEngine.Services.Implementation.Tools
{
    public class PowerShellTool : ITool, IDisposable
    {
        private readonly RunspacePool _runspacePool;
        private readonly IToolStatusNotifier _notifier;


        public PowerShellTool(IToolStatusNotifier notifier)
        {
            // 1) Build an InitialSessionState that auto-imports Windows PowerShell’s Microsoft.PowerShell.Management
            var iss = InitialSessionState.CreateDefault();
            _notifier = notifier;
            /*var iss1 = InitialSessionState.CreateFromSessionConfigurationFile(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "System32",
                    "WindowsPowerShell",
                    "v1.0",
                    "Microsoft.PowerShell.Core.pssc"
                )
            );*/
            //var iss = InitialSessionState.CreateDefault();
            iss.ImportPSModule(new[] { "Microsoft.PowerShell.Management" });
            //iss.ImportPSModulesFromPath = true;
            //iss.ImportPSSnapIn
            // 2) Create the pool from that ISS
            _runspacePool = RunspaceFactory.CreateRunspacePool(
             
                //minRunspaces: 1,
                //maxRunspaces: 5,
                   iss//,
                //host: null
            );
            _runspacePool.ThreadOptions = PSThreadOptions.UseNewThread;
            _runspacePool.Open();
        }


        [Description("Executes arbitrary PowerShell script text  (must use PowerShell 7+ so Get-CimInstance never Get-WmiObject)")]
        public async Task<IEnumerable<string>> PowerShellScriptAsync(
            [Description("The PowerShell script to run")] string script,
            [Description("Optional named parameters")] IDictionary<string, object>? parameters = null
        )
        {
            using var ps = PowerShell.Create();
            ps.RunspacePool = _runspacePool;

            if (parameters != null)
            {
                var table = new Hashtable();
                foreach (var kv in parameters)
                    table.Add(kv.Key, kv.Value);
                ps.AddParameters(table);
            }

            ps.AddScript(script);
            var results = await Task.Run(() => ps.Invoke());

            if (ps.HadErrors)
                throw new InvalidOperationException(
                    string.Join(Environment.NewLine, ps.Streams.Error.Select(e => e.ToString()))
                );

            return results.Select(r => r.ToString());
        }

    
      /*  [Description("Invokes a single PowerShell cmdlet with named parameters (must use PowerShell 7+ so Get-CimInstance never Get-WmiObject)")]
        public async Task<IEnumerable<string>> PowerShellAsync(
              [Description("The CIM cmdlet")]
    string cmdlet,
               [Description("Optional named parameters")]
    IDictionary<string, object>? parameters = null
)
        {

            try
            {

            using var ps = PowerShell.Create();
            ps.RunspacePool = _runspacePool;

            // ——— BEGIN Redirect WMI → CIM ———
            // build a hashtable of parameters, remapping "Class" → "ClassName" if needed
            var table = new Hashtable();
            if (parameters != null)
            {
                foreach (var kv in parameters)
                {
                    var key = kv.Key;
                    if (cmdlet.Equals("Get-WmiObject", StringComparison.OrdinalIgnoreCase)
                     && key.Equals("Class", StringComparison.OrdinalIgnoreCase))
                    {
                        key = "ClassName";
                    }
                    table[key] = kv.Value;
                }
            }

            // swap out the cmdlet itself
            var effectiveCmdlet = cmdlet.Equals("Get-WmiObject", StringComparison.OrdinalIgnoreCase)
                                 ? "Get-CimInstance"
                                 : cmdlet;
            Console.WriteLine($"[PS] Invoking: {effectiveCmdlet}");
            foreach (DictionaryEntry entry in table)
                Console.WriteLine($"[PS]   Param: {entry.Key} = {entry.Value}");

            // add it
            var command = ps.AddCommand(effectiveCmdlet)
                            .AddParameters(table);
            // ———  END Redirect  ———

      


            ps.Streams.Error.DataAdded += (sender, args) =>   {
                var err = ((PSDataCollection<ErrorRecord>)sender)[args.Index];
                Console.WriteLine($"[PS:ERR] {err.Exception.Message}");
                  }
            ;
            var results = await Task.Run(() => ps.Invoke());

            if (ps.HadErrors)
                throw new InvalidOperationException(
                    string.Join(Environment.NewLine, ps.Streams.Error.Select(e => e.ToString()))
                );


        
            return results.Select(r => r.ToString());

            }
            catch (Exception ex)
            {
                string msg = $"The tool call returned the following exception {ex.Message}, coming from the stack trace {ex.StackTrace.Substring(0,20)} if the error cannot be resolved by you recalling the tool then output error details to the user and how to fix it. ";

                return new List<string>() { msg};
            }
        }
      */

        public void Dispose()
        {
            _runspacePool.Close();
            _runspacePool.Dispose();
        }
    }
}
