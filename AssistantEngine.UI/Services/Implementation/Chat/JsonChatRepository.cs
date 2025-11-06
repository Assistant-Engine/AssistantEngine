using AssistantEngine.UI.Services;
using AssistantEngine.UI.Services.Implementation.Models.Chat;
using AssistantEngine.UI.Services.Models.Chat;
using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Globalization; // ADD

using System.Text.Json.Serialization;

namespace AssistantEngine.UI.Services.Implementation.Chat
{
    /// <summary>
    /// JSONL chat repository that persists native ChatSession objects using System.Text.Json.
    /// - Writes native models (no DTOs)
    /// - Reads native; falls back to old DTO line format if present
    /// - Handles AIContent polymorphism via attributes on Microsoft.Extensions.AI.AIContent
    /// </summary>
    public sealed class JsonChatRepository : IChatRepository
    {
        public event Action? SessionsChanged;
        public IReadOnlyDictionary<string, string> ChatSessionNames => _sessionNames;

        private readonly Dictionary<string, string> _sessionNames = new();
        private readonly string _path;
        private readonly JsonSerializerOptions _json;

        public bool Initialised { get; private set; }

        //we can remove the dto logic for simplification now
        public JsonChatRepository(IAppConfigStore config)
        {
            _path = Path.Combine(config.AppDataDirectory, "chats.jsonl");
            _json = new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                PropertyNameCaseInsensitive = true
            };
            _json.Converters.Add(new JsonStringEnumConverter());
        }

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            if (Initialised) return;
            await ListAllAsync(ct);
            SessionsChanged?.Invoke();
            Initialised = true;
        }

        public async Task SaveAsync(ChatSession session, CancellationToken ct = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var line = JsonSerializer.Serialize(session, _json);

            var lines = File.Exists(_path)
                ? await File.ReadAllLinesAsync(_path, ct)
                : Array.Empty<string>();

            var updated = new List<string>(capacity: lines.Length + 1);
            foreach (var l in lines)
            {
                var id = TryPeekId(l);
                if (!string.Equals(id, session.Id, StringComparison.Ordinal))
                    updated.Add(l);
            }
            updated.Add(line);

            await File.WriteAllLinesAsync(_path, updated, ct);

            _sessionNames[session.Id] = session.Title;
            SessionsChanged?.Invoke();
        }

        public async Task<ChatSession?> LoadAsync(string sessionId, CancellationToken ct = default)
        {
            if (!File.Exists(_path)) return null;

            await foreach (var raw in File.ReadLinesAsync(_path, ct))
            {
                var id = TryPeekId(raw);
                if (!string.Equals(id, sessionId, StringComparison.Ordinal)) continue;

                if (TryDeserializeNative(raw, out var native))
                    return native;
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
                ChatSession? model = null;

                if (TryDeserializeNative(raw, out var native))
                    model = native;

                if (model is null) continue;

                byId[model.Id] = model;
                _sessionNames[model.Id] = model.Title;
            }

            SessionsChanged?.Invoke();
            return byId.Values;
        }

        public async Task ClearSessionAsync(string sessionId, CancellationToken ct = default)
        {
            if (!File.Exists(_path)) return;

            var lines = await File.ReadAllLinesAsync(_path, ct);
            var filtered = lines.Where(raw =>
            {
                var id = TryPeekId(raw);
                return !string.Equals(id, sessionId, StringComparison.Ordinal);
            });

            await File.WriteAllLinesAsync(_path, filtered, ct);

            _sessionNames.Remove(sessionId);
            SessionsChanged?.Invoke();
        }


        private bool TryDeserializeNative(string raw, out ChatSession? model)
        {
            try
            {
                model = JsonSerializer.Deserialize<ChatSession>(raw, _json);
                if (model is null || model.Messages is null) return model is not null;
                NormalizeAfterLoad(model); // ADD: coerce date-like values to DateTime (UTC)
                return true;
            }
            catch
            {
                model = null;
                return false;
            }
        }

        private static void NormalizeAfterLoad(ChatSession model)
        {
            foreach (var m in model.Messages ?? Enumerable.Empty<ChatMessage>())
            {
                Coerce(m.AdditionalProperties, "StartedAt", "FinishedAt", "CreatedAt", "StartedThinkingAt", "FinishedThinkingAt");
                foreach (var c in m.Contents ?? Enumerable.Empty<AIContent>())
                    Coerce(c.AdditionalProperties, "StartedAt", "FinishedAt", "CreatedAt", "StartedThinkingAt", "FinishedThinkingAt");
            }

            static void Coerce(IDictionary<string, object?>? dict, params string[] keys)
            {
                if (dict is null) return;
                foreach (var k in keys)
                {
                    if (!dict.TryGetValue(k, out var v) || v is DateTime) continue;
                    var dt = CoerceUtc(v);
                    if (dt.HasValue) dict[k] = dt.Value; // replace with DateTime (UTC)
                }
            }
        }

        private static DateTime? CoerceUtc(object? v)
        {
            switch (v)
            {
                case null:
                    return null;

                case DateTime dt:
                    return dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();

                case DateTimeOffset dto:
                    return dto.UtcDateTime;

                case string s:
                    // ISO8601/round-trip; accepts "2025-11-05T08:28:41.5633321Z" etc.
                    if (DateTimeOffset.TryParse(
                            s,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                            out var parsedS))
                        return parsedS.UtcDateTime;
                    // Unix seconds/ms as string
                    if (long.TryParse(s, out var unix))
                        return FromUnix(unix);
                    return null;

                case JsonElement je:
                    if (je.ValueKind == JsonValueKind.String)
                    {
                        var str = je.GetString();
                        if (!string.IsNullOrEmpty(str) &&
                            DateTimeOffset.TryParse(
                                str,
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                                out var parsedJeS))
                            return parsedJeS.UtcDateTime;
                    }
                    if (je.ValueKind == JsonValueKind.Number)
                    {
                        if (je.TryGetInt64(out var unixNum))
                            return FromUnix(unixNum);
                        if (je.TryGetDouble(out var unixD))
                            return FromUnix((long)unixD);
                    }
                    return null;

                default:
                    return null;
            }

            static DateTime FromUnix(long v)
            {
                // Heuristic: 13+ digits => ms; 10 digits => seconds
                if (v > 9_999_999_999) // ms
                    return DateTimeOffset.FromUnixTimeMilliseconds(v).UtcDateTime;
                return DateTimeOffset.FromUnixTimeSeconds(v).UtcDateTime;
            }
        }


   
        private string? TryPeekId(string raw)
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("Id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                    return idProp.GetString();

                // DTO fallback casing
                if (doc.RootElement.TryGetProperty("id", out var idLower) && idLower.ValueKind == JsonValueKind.String)
                    return idLower.GetString();
            }
            catch { /* ignore line parse errors */ }
            return null;
        }


    }

}
