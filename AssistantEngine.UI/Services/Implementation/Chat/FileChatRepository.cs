using AssistantEngine.UI.Pages.Chat;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using OllamaSharp.Tools;
using SQLitePCL;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using AssistantEngine.UI.Services;
using AssistantEngine.UI.Services.Models.Chat;
using AssistantEngine.UI.Services.Implementation.Models.Chat;


namespace AssistantEngine.UI.Services.Implementation.Chat
{
    public class IncludeIgnoredResolver : DefaultContractResolver
    {
        protected override JsonProperty CreateProperty(
            MemberInfo member,
            MemberSerialization memberSerialization)
        {
            var prop = base.CreateProperty(member, memberSerialization);
            // flip off any [JsonIgnore] on ChatMessageItem
            if (prop.DeclaringType == typeof(ChatMessageItem))
                prop.Ignored = false;
            return prop;
        }
    }
    public class FileChatRepository : IChatRepository
    {
    
        public event Action SessionsChanged;
        private readonly string _path;
        private readonly Dictionary<string, string> _sessionNames = new();
        public IReadOnlyDictionary<string, string> ChatSessionNames
            => _sessionNames;

        public bool Initialised { get; private set; }
        private static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new IncludeIgnoredResolver()
        };
   
        public FileChatRepository(IAppConfigStore config)
            => _path = Path.Combine(config.AppDataDirectory,"chats.jsonl") ?? "chats.jsonl";

      /*  public async Task SaveAsync(ChatSession session, CancellationToken ct = default)
        {
            var dto = session.ToDto();
            var line = JsonConvert.SerializeObject(dto, _jsonSettings);
            await File.AppendAllTextAsync(_path, line + "\n", ct);

            _sessionNames[session.Id] = session.Title;
            SessionsChanged?.Invoke();
        }*/

        public async Task SaveAsync(ChatSession session, CancellationToken ct = default)
        {
            var dto = session.ToDto();
            var line = JsonConvert.SerializeObject(dto, _jsonSettings);

            var lines = File.Exists(_path)
                ? await File.ReadAllLinesAsync(_path, ct)
                : Array.Empty<string>();

            var updated = lines
                .Where(l =>
                {
                    var s = JsonConvert.DeserializeObject<ChatSessionDto>(l, _jsonSettings);
                    return s?.Id != session.Id;
                })
                .ToList();

            updated.Add(line);
            await File.WriteAllLinesAsync(_path, updated, ct);

            _sessionNames[session.Id] = session.Title;
            SessionsChanged?.Invoke();
        }

        public async Task ClearSessionAsync(string sessionId, CancellationToken ct = default)
        {
            if (!File.Exists(_path)) return;

            var lines = await File.ReadAllLinesAsync(_path, ct);
            var filtered = lines
                .Where(raw =>
                {
                    var s = JsonConvert.DeserializeObject<ChatSession>(raw);
                    return s is not null && s.Id != sessionId;
                });
            await File.WriteAllLinesAsync(_path, filtered, ct);

            _sessionNames.Remove(sessionId); SessionsChanged?.Invoke();
        }


        public async Task<ChatSession?> LoadAsync(string sessionId, CancellationToken ct = default)
        {
            if (!File.Exists(_path)) return null;

            await foreach (var raw in File.ReadLinesAsync(_path, ct))
            {
                var dto = JsonConvert.DeserializeObject<ChatSessionDto>(raw, _jsonSettings);
                if (dto?.Id == sessionId)
                    return dto.ToModel();
            }
            return null;
        }

        public async Task<IEnumerable<ChatSession>> ListAllAsync(CancellationToken ct = default)
        {
            _sessionNames.Clear();
            if (!File.Exists(_path)) return Enumerable.Empty<ChatSession>();

            var byId = new Dictionary<string, ChatSession>();
            await foreach (var raw in File.ReadLinesAsync(_path, ct))
            {
                var dto = JsonConvert.DeserializeObject<ChatSessionDto>(raw, _jsonSettings);
                if (dto is null) continue;

                var model = dto.ToModel();
                byId[model.Id] = model;
                _sessionNames[model.Id] = model.Title;
            }
            return byId.Values;
        }

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            if (Initialised) return;
            await ListAllAsync(ct); SessionsChanged?.Invoke();
            Initialised = true;
        }
    }

}
