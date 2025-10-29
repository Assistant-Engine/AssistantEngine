using AssistantEngine.UI.Services;
using AssistantEngine.UI.Services.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AssistantEngine.UI.Services.Implementation.Config
{
    public enum AppConfigLoadStatus
    {
        Loaded,
        FreshDefault,
        RecoveredFromInvalid,
        VectorStoreMoved
    }

    public sealed class AppConfigStore : IAppConfigStore
    {
        public AppConfigLoadStatus LastStatus { get; private set; } = AppConfigLoadStatus.Loaded;
        public List<string> LastNotes { get; } = new();

        private readonly ConfigStorageOptions _opts;
        private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
        private readonly object _gate = new();
        private AppConfig _current;
        public string AppDataDirectory => Path.Combine(_opts.DefaultDataRoot, "App_Data");
        public AppConfig Current { get { lock (_gate) return _current; } }

        public event EventHandler<AppConfig>? Changed;
        public string ConfigFilePath => _opts.ConfigFilePath;

        public void SaveSync(AppConfig config)
        {
            var copy = FillMissingWithDefaults(config, _opts.DefaultDataRoot);
            NormalizeAndEnsurePaths(ref copy, _opts.DefaultDataRoot);

            var tmp = _opts.ConfigFilePath + ".tmp";
            var json = JsonSerializer.Serialize(copy, _json);
            File.WriteAllText(tmp, json);
            File.Move(tmp, _opts.ConfigFilePath, overwrite: true);

            lock (_gate) _current = copy;
            _ = Task.Run(() => Changed?.Invoke(this, copy));
        }
        public AppConfigStore(ConfigStorageOptions options)
        {
            _opts = options;
            Directory.CreateDirectory(AppDataDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(_opts.ConfigFilePath)!);

            _current = CreateDefaults(_opts.DefaultDataRoot);
            LastStatus = AppConfigLoadStatus.FreshDefault;
            LastNotes.Add("No config loaded yet; defaults in memory.");

            if (File.Exists(_opts.ConfigFilePath))
            {
                try
                {
                    var raw = File.ReadAllText(_opts.ConfigFilePath);
                    var loaded = JsonSerializer.Deserialize<AppConfig>(raw);
                    if (loaded is not null)
                    {
                        _current = FillMissingWithDefaults(loaded, _opts.DefaultDataRoot);
                        LastStatus = AppConfigLoadStatus.Loaded;
                        LastNotes.Add("Config loaded successfully.");
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        var backup = _opts.ConfigFilePath + $".bad-{DateTime.UtcNow:yyyyMMddHHmmss}.json";
                        File.Move(_opts.ConfigFilePath, backup);
                        LastNotes.Add($"Invalid JSON detected; original backed up to: {backup}");
                    }
                    catch { /* best-effort backup */ }

                    _current = CreateDefaults(_opts.DefaultDataRoot);
                    LastStatus = AppConfigLoadStatus.RecoveredFromInvalid;
                    LastNotes.Add($"Recovered with defaults due to invalid JSON: {ex.GetType().Name}");
                }
            }

            NormalizeAndEnsurePaths(ref _current, _opts.DefaultDataRoot);

        }
        private void EnsureVectorStoreWritable(ref AppConfig cfg, string root)
        {
            var tryPath = cfg.VectorStoreFilePath;

            bool CanOpen(string p)
            {
                try
                {
                    var dir = Path.GetDirectoryName(p);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    using var fs = new FileStream(p, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                    return true;
                }
                catch { return false; }
            }

            if (!CanOpen(tryPath))
            {
                var fallback = Path.Combine(AppDataDirectory, Path.GetFileName(tryPath));
                if (!Path.IsPathRooted(fallback))
                    fallback = Path.GetFullPath(Path.Combine(root, fallback));

                if (CanOpen(fallback))
                {
                    cfg.VectorStoreFilePath = fallback;
                    LastStatus = AppConfigLoadStatus.VectorStoreMoved;
                    LastNotes.Add($"Vector store path not writable; moved to: {fallback}");
                }
                else
                {
                    // Keep original; downstream features should gate on failure.
                    LastNotes.Add("Vector store path not writable; fallback also failed.");
                }
            }
        }
        public async Task SaveAsync(AppConfig config, CancellationToken ct = default)
        {
            var copy = FillMissingWithDefaults(config, _opts.DefaultDataRoot);
            NormalizeAndEnsurePaths(ref copy, _opts.DefaultDataRoot);

            var tmp = _opts.ConfigFilePath + ".tmp";
            var json = JsonSerializer.Serialize(copy, _json);
            await File.WriteAllTextAsync(tmp, json, ct);
            File.Move(tmp, _opts.ConfigFilePath, overwrite: true);

            lock (_gate) _current = copy;
            Changed?.Invoke(this, copy);
        }

        public Task ReloadAsync(CancellationToken ct = default)
        {
            AppConfig cfg = File.Exists(_opts.ConfigFilePath)
                ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_opts.ConfigFilePath)) ?? CreateDefaults(_opts.DefaultDataRoot)
                : CreateDefaults(_opts.DefaultDataRoot);

            cfg = FillMissingWithDefaults(cfg, _opts.DefaultDataRoot);
            NormalizeAndEnsurePaths(ref cfg, _opts.DefaultDataRoot);

            lock (_gate) _current = cfg;
            Changed?.Invoke(this, cfg);
            return Task.CompletedTask;
        }

        public Task ResetToDefaultsAsync(CancellationToken ct = default)
        {
            var defaults = CreateDefaults(_opts.DefaultDataRoot);
            return SaveAsync(defaults, ct);
        }

        public Task UnloadAsync(CancellationToken ct = default)
        {
            var defaults = CreateDefaults(_opts.DefaultDataRoot);
            NormalizeAndEnsurePaths(ref defaults, _opts.DefaultDataRoot);
            lock (_gate) _current = defaults;
            Changed?.Invoke(this, defaults);
            return Task.CompletedTask;
        }

        private static AppConfig CreateDefaults(string root)
        {
            var dataDir = Path.Combine(root, "App_Data");
            var modelsDir = Path.Combine(dataDir, "Models"); // <-- was under AppContext.BaseDirectory (read-only on Mac)
            return new AppConfig
            {
                AppDBFilePath = Path.Combine(dataDir, "assistantengine.db"),
                VectorStoreFilePath = Path.Combine(dataDir, "vector-store-main.db"),
                ModelFilePath = modelsDir
            };
        }


        private static AppConfig FillMissingWithDefaults(AppConfig cfg, string root)
        {
            var d = CreateDefaults(root);
            //cfg.OllamaUrl = string.IsNullOrWhiteSpace(cfg.OllamaUrl) ? d.OllamaUrl : cfg.OllamaUrl;

            cfg.VectorStoreFilePath = string.IsNullOrWhiteSpace(cfg.VectorStoreFilePath) ? d.VectorStoreFilePath : cfg.VectorStoreFilePath;
            cfg.AppDBFilePath = string.IsNullOrWhiteSpace(cfg.AppDBFilePath) ? d.AppDBFilePath : cfg.AppDBFilePath;
            cfg.ModelFilePath = string.IsNullOrWhiteSpace(cfg.ModelFilePath) ? d.ModelFilePath : cfg.ModelFilePath;
            return cfg;
        }

        private void NormalizeAndEnsurePaths(ref AppConfig cfg, string root)
        {
            string Root(string p) => Path.IsPathRooted(p) ? p : Path.GetFullPath(Path.Combine(root, p));

            // ALWAYS root under writable data root
            cfg.VectorStoreFilePath = Root(cfg.VectorStoreFilePath);
            cfg.ModelFilePath = Root(cfg.ModelFilePath);
            cfg.AppDBFilePath = Root(cfg.AppDBFilePath);

            // Ensure parents exist in writable area
            var vsDir = Path.GetDirectoryName(cfg.VectorStoreFilePath);
            if (!string.IsNullOrEmpty(vsDir)) Directory.CreateDirectory(vsDir);

            Directory.CreateDirectory(cfg.ModelFilePath); // it's a directory path

            Console.WriteLine("[AIO] DefaultDataRoot=" + _opts.DefaultDataRoot);
            Console.WriteLine("[AIO] AppDataDirectory=" + AppDataDirectory);
            Console.WriteLine("[AIO] VectorStoreFilePath=" + cfg.VectorStoreFilePath);
            Console.WriteLine("[AIO] AppDbFilePath=" + cfg.AppDBFilePath);
            Console.WriteLine("[AIO] ModelFilePath=" + cfg.ModelFilePath);

            EnsureVectorStoreWritable(ref cfg, root);
        }

    }
}
