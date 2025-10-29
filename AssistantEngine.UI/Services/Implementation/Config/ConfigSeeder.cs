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
        public static void SeedShippedModelsIntoAppData(IAppConfigStore store)
        {
            var appData = store.AppDataDirectory;
            var destDir = Path.Combine(appData, "Models");
            Directory.CreateDirectory(destDir);
     
            var asm = typeof(ConfigSeeder).Assembly;
            const string Prefix = "Config.Models.";

            var embeddedModels = asm.GetManifestResourceNames()
                .Where(n => n.Contains(Prefix, StringComparison.OrdinalIgnoreCase) &&
                            n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var overwriteDuplicates = false; // true = overwrite same-Id + same-name files; false = current behavior

            foreach (var res in embeddedModels)
            {
                var idx = res.IndexOf(Prefix, StringComparison.OrdinalIgnoreCase);
                var outName = idx >= 0 ? res.Substring(idx + Prefix.Length) : Path.GetFileName(res);
                var outPath = Path.Combine(destDir, outName);
                using var s = asm.GetManifestResourceStream(res)!;
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                ms.Position = 0;

                // If a file with same model Id already exists, skip writing to avoid duplicates
                string? newId = null;
                try
                {
                    using var jd = System.Text.Json.JsonDocument.Parse(ms, new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true });
                    newId = jd.RootElement.TryGetProperty("Id", out var idEl) ? idEl.GetString() : null;
                }
                catch { /* ignore parse errors; fallback to filename check */ }
                finally { ms.Position = 0; }

                if (!string.IsNullOrWhiteSpace(newId))
                {
                    var duplicateIdExists = Directory.EnumerateFiles(destDir, "*.json")
                        .Any(f =>
                        {
                            try
                            {
                                var txt = File.ReadAllText(f);
                                using var jd2 = System.Text.Json.JsonDocument.Parse(txt);
                                return jd2.RootElement.TryGetProperty("Id", out var idEl2) &&
                                       string.Equals(idEl2.GetString(), newId, StringComparison.OrdinalIgnoreCase);
                            }
                            catch { return false; }
                        });

                    if (duplicateIdExists && !overwriteDuplicates)
                    {
                        Console.WriteLine($"[seed] Skipped '{outName}' (Id '{newId}' already present).");
                        continue;
                    }

                    if (duplicateIdExists && overwriteDuplicates)
                    {
                        foreach (var fpath in Directory.EnumerateFiles(destDir, "*.json"))
                        {
                            try
                            {
                                var txt = File.ReadAllText(fpath);
                                using var jd2 = System.Text.Json.JsonDocument.Parse(txt);
                                if (jd2.RootElement.TryGetProperty("Id", out var idEl2) &&
                                    string.Equals(idEl2.GetString(), newId, StringComparison.OrdinalIgnoreCase))
                                {
                                    File.Delete(fpath);
                                }
                            }
                            catch { /* ignore parse errors */ }
                        }
                    }
                }


                if (!overwriteDuplicates && File.Exists(outPath)) continue;

                using var f = File.Create(outPath);
                ms.CopyTo(f);
                Console.WriteLine($"[seed] Wrote {outName}");
            }


            // validate duplicates (non-fatal)
            try
            {
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in Directory.EnumerateFiles(destDir, "*.json"))
                {
                    var txt = File.ReadAllText(file);
                    var id = System.Text.Json.JsonDocument.Parse(txt).RootElement.GetProperty("Id").GetString();
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    if (!ids.Add(id))
                        Console.WriteLine($"[seed] Warning: duplicate Id '{id}' in {Path.GetFileName(file)} (keeping first).");
                }
            }
            catch { }

            // redirect if needed
            var current = store.Current.ModelFilePath;
            var needsRedirect = string.IsNullOrWhiteSpace(current) ||
                                IsUnder(current, AppContext.BaseDirectory) ||
                                !IsWritable(current);

            if (needsRedirect && !string.Equals(current, destDir, StringComparison.OrdinalIgnoreCase))
            {
                var cfg = store.Current;
                cfg.ModelFilePath = destDir;
                if (store is AppConfigStore impl) impl.SaveSync(cfg);
                else throw new InvalidOperationException("IAppConfigStore must be AppConfigStore to save synchronously.");
            }
        }
        static void WipeDirectory(string destDir) //TEMP
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(destDir, "*.json"))
                    File.Delete(f);
                Console.WriteLine("[seed] Cleared existing *.json in Models.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[seed] Clear failed: " + ex.Message);
            }
        }
        static void CopyResIfMissing(string logicalNameSlash, string destDir)
        {
            var asm = typeof(ConfigSeeder).Assembly;
            var names = asm.GetManifestResourceNames();

            // support both "Config/Models/..." and "Config.Models...." embeddings
            string slash = logicalNameSlash;
            string dotty = logicalNameSlash.Replace('/', '.');

            var res = names.FirstOrDefault(n =>
                         n.EndsWith(slash, StringComparison.OrdinalIgnoreCase) ||
                         n.EndsWith(dotty, StringComparison.OrdinalIgnoreCase));

            if (res is null) { Console.WriteLine($"[seed] Embedded missing: {logicalNameSlash}"); return; }

            var outName = Path.GetFileName(logicalNameSlash); // e.g. "RemoteAssistantPro.json"
            var outPath = Path.Combine(destDir, outName);
            if (File.Exists(outPath)) return;

            using var s = asm.GetManifestResourceStream(res)!;
            using var f = File.Create(outPath);
            s.CopyTo(f);
            Console.WriteLine($"[seed] Wrote {outName}");
        }

        public static void SeedShippedModelsIntoAppDataOld(IAppConfigStore store, string? shippedModelsRoot = null)
        {
            try
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
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
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
