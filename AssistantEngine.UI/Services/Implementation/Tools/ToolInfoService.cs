using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AssistantEngine.UI.Services.Implementation.Tools
{


    public sealed class ToolInfoService
    {
        public RenderFragment Build(AIFunction fn) => b =>
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(fn.Description))
                sb.Append("<h6 class='mb-2'>Description</h6><p class='mb-3'>").Append(fn.Description).Append("</p>");

            if (TryExtractParams(fn.JsonSchema, out var @params))
            {
                sb.Append("<h6 class='mb-2'>Parameters</h6><ul class='list-unstyled small mb-3'>");
                foreach (var p in @params)
                {
                    sb.Append("<li class='mb-2'><b>").Append(p.Name).Append("</b>");
                    if (!string.IsNullOrWhiteSpace(p.Type)) sb.Append("<span class='text-muted'> (").Append(p.Type).Append(")</span>");
                    if (p.Required) sb.Append("<span class='badge bg-secondary ms-1'>required</span>");
                    if (!string.IsNullOrWhiteSpace(p.Description)) sb.Append("<div>").Append(p.Description).Append("</div>");
                    if (!string.IsNullOrWhiteSpace(p.Default)) sb.Append("<div class='text-muted'>default: ").Append(p.Default).Append("</div>");
                    sb.Append("</li>");
                }
                sb.Append("</ul>");
            }

            if (fn.AdditionalProperties?.Any() == true)
            {
                sb.Append("<h6 class='mb-2'>Additional Properties</h6><ul class='list-unstyled small'>");
                foreach (var kv in fn.AdditionalProperties)
                    sb.Append("<li><span class='text-muted'>").Append(kv.Key).Append(":</span> <span>").Append(kv.Value).Append("</span></li>");
                sb.Append("</ul>");
            }

            b.AddMarkupContent(0, sb.ToString());
        };

        record ParamInfo(string Name, string? Type, string? Description, bool Required, string? Default);

        static bool TryExtractParams(JsonElement schema, out List<ParamInfo> list)
        {
            list = new();
            if (schema.ValueKind != JsonValueKind.Object) return false;

            var requiredSet = new HashSet<string>(StringComparer.Ordinal);
            if (schema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
                foreach (var r in req.EnumerateArray())
                    if (r.ValueKind == JsonValueKind.String) requiredSet.Add(r.GetString()!);

            if (!schema.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var prop in props.EnumerateObject())
            {
                var name = prop.Name;
                var val = prop.Value;

                var type = GetTypeString(val);
                var desc = val.TryGetProperty("description", out var dEl) && dEl.ValueKind == JsonValueKind.String ? dEl.GetString() : null;
                var def = val.TryGetProperty("default", out var defEl) ? SafeScalarToString(defEl) : null;
                var reqd = requiredSet.Contains(name);

                list.Add(new ParamInfo(name, type, desc, reqd, def));
            }

            return list.Count > 0;
        }

        static string? GetTypeString(JsonElement el)
        {
            if (el.TryGetProperty("type", out var tEl))
            {
                if (tEl.ValueKind == JsonValueKind.String) return tEl.GetString();
                if (tEl.ValueKind == JsonValueKind.Array)
                    return string.Join(" | ", tEl.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()));
            }
            if (el.TryGetProperty("oneOf", out var one) && one.ValueKind == JsonValueKind.Array) return "oneOf";
            if (el.TryGetProperty("anyOf", out var any) && any.ValueKind == JsonValueKind.Array) return "anyOf";
            if (el.TryGetProperty("allOf", out var all) && all.ValueKind == JsonValueKind.Array) return "allOf";
            return null;
        }

        static string? SafeScalarToString(JsonElement el) => el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

}
