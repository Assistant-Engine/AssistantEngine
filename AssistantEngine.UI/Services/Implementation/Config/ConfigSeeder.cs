using AssistantEngine.UI.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssistantEngine.UI.Services.Implementation.Config
{
    public static class ConfigSeeder
    {
        /// <summary>
        /// Copies shipped model JSONs from <paramref name="shippedModelsRoot"/> into
        /// &lt;AppDataDirectory&gt;/Models on first run, and points ModelFilePath there.
        /// </summary>
        /// 
   

        public static void SeedShippedModelsIntoAppData(IAppConfigStore store, string? shippedModelsRoot = null)
        {
            shippedModelsRoot ??= Path.Combine(AppContext.BaseDirectory, "Config", "Models");

            var appData = store.AppDataDirectory;
            var dest = Path.Combine(appData, "Models");
            Directory.CreateDirectory(dest);

            var current = store.Current.ModelFilePath;

            // Decide where to point: if empty, shipped, or unwritable -> AppData
            var needsRedirect =
                string.IsNullOrWhiteSpace(current) ||
                IsUnder(current, AppContext.BaseDirectory) ||
                !IsWritable(current);

            if (Directory.Exists(shippedModelsRoot))
            {
                foreach (var file in Directory.EnumerateFiles(shippedModelsRoot, "*.json"))
                {
                    var target = Path.Combine(dest, Path.GetFileName(file));
                    if (!File.Exists(target))
                        File.Copy(file, target);
                }
            }

            if (needsRedirect && !string.Equals(current, dest, StringComparison.OrdinalIgnoreCase))
            {
                var cfg = store.Current;
                cfg.ModelFilePath = dest;

                if (store is AppConfigStore impl) impl.SaveSync(cfg);
                else throw new InvalidOperationException("IAppConfigStore must be AppConfigStore to save synchronously.");
            }
        }

        private static bool IsWritable(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                var testFile = Path.Combine(path, ".writetest");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                return true;
            }
            catch
            {
                return false;
            }
        }
        private static bool IsUnder(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;
            var full = Path.GetFullPath(path);
            var fullRoot = Path.GetFullPath(root);
            return full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }

      
    }
}
