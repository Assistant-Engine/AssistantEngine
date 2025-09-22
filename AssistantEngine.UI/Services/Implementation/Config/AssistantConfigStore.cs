using AssistantEngine.UI.Services.Models;
using Newtonsoft.Json;

namespace AssistantEngine.UI.Services.Implementation.Config
{
    // 2) Implement it with Newtonsoft.Json
    public class JsonAssistantConfigStore : IAssistantConfigStore
    {
        private readonly Dictionary<string, (AssistantConfig Config, string Path)> _map;
        private readonly string _folder;
        private readonly JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };



        public JsonAssistantConfigStore(string folder)
        {
            _folder = folder;
            _map = Directory
                .GetFiles(folder, "*.json")
                .Select(path =>
                {
                    var cfg = JsonConvert.DeserializeObject<AssistantConfig>(
                        File.ReadAllText(path), _settings)!;

                    // Force a clean list (dedupe + break default initializer merge)
                    cfg.EnabledFunctions = cfg.EnabledFunctions?
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList() ?? new List<string>();

                    return (cfg.Id, (cfg, path));
                })
                .ToDictionary(t => t.Id, t => t.Item2);

           
        }

        public IReadOnlyCollection<AssistantConfig> GetAll()
            => _map.Values.Select(x => x.Config).ToList();

        public AssistantConfig? GetById(string id)
            => _map.TryGetValue(id, out var v) ? v.Config : null;

       /* public async Task UpdateAsync(AssistantConfig updated)
        {
            // figure out if we're adding or updating
            var isExisting = _map.TryGetValue(updated.Id, out var entry);
            // pick existing path or build a new one
            var path = isExisting
                ? entry.Path
                : Path.Combine(_folder, $"{updated.Id}.json");

            // 1) stash tools
            var backup = updated.AssistantModel.Tools;
            var before = string.Join(", ", updated.EnabledFunctions);
            var distinct = string.Join(", ", updated.EnabledFunctions?.Distinct());
       

            updated.EnabledFunctions = updated.EnabledFunctions?.Distinct().ToList();
            // 2) strip them out for storage
            updated.AssistantModel.Tools = null;
            // 3) serialize & write
            //updated.EnabledFunctions
            var json = JsonConvert.SerializeObject(updated, _settings);
            await File.WriteAllTextAsync(path, json);
            // 4) restore in‑memory and upsert
            updated.AssistantModel.Tools = backup;
            _map[updated.Id] = (updated, path);
        }*/

        public async Task UpdateAsync(string originalId, AssistantConfig updated)
        {
            var hasOriginal = _map.TryGetValue(originalId, out var oldEntry);
            var oldPath = hasOriginal ? oldEntry.Path : null;
            var newPath = Path.Combine(_folder, $"{updated.Id}.json");

            // 1) stash & normalize (same as before)
            var backup = updated.AssistantModel.Tools;
            updated.EnabledFunctions = updated.EnabledFunctions?.Distinct().ToList();
            updated.AssistantModel.Tools = null;

            var json = JsonConvert.SerializeObject(updated, _settings);

            // 2) write file (handle rename)
            if (hasOriginal && !string.Equals(originalId, updated.Id, StringComparison.OrdinalIgnoreCase))
            {
                await File.WriteAllTextAsync(newPath, json);
                if (!string.IsNullOrEmpty(oldPath) && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath))
                    File.Delete(oldPath);

                _map.Remove(originalId);
            }
            else
            {
                var path = hasOriginal ? oldPath! : newPath;
                await File.WriteAllTextAsync(path, json);
                newPath = path; // ensure mapping uses actual path
            }

            // 3) restore in-memory & upsert map
            updated.AssistantModel.Tools = backup;
            _map[updated.Id] = (updated, newPath);
        }

        // keep the old signature for backward compatibility
        public async Task UpdateAsync(AssistantConfig updated)
        {
            await UpdateAsync(updated.Id, updated);
        }

    }

    // 1) Define an interface
    public interface IAssistantConfigStore
    {
        IReadOnlyCollection<AssistantConfig> GetAll();
        AssistantConfig? GetById(string id);
        Task UpdateAsync(AssistantConfig updated);
        Task UpdateAsync(string originalId, AssistantConfig updated);
    }

}
