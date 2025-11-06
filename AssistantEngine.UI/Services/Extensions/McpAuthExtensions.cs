using AssistantEngine.UI.Services.Models;
// McpAuthExtensions.cs
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;


namespace AssistantEngine.UI.Services.Extensions
{
    static class FormDictHelpers
    {
        public static Dictionary<string, string> AlsoAddIf(this Dictionary<string, string> d, bool cond, string k, string v)
        {
            if (cond) d[k] = v;
            return d;
        }
    }
    public static class McpAuthExtensions
    {
        public static void ApplyAuthHeaders(this HttpRequestMessage req, McpConnectorConfig c)
        {
            if (c.Auth == AuthType.BearerOrApiKey && !string.IsNullOrWhiteSpace(c.AuthToken))
            {
                if (string.Equals(c.ApiKeyHeaderName, "Authorization", StringComparison.OrdinalIgnoreCase))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", c.AuthToken);
                else
                    req.Headers.TryAddWithoutValidation(c.ApiKeyHeaderName, c.AuthToken);
            }
            else if (c.Auth == AuthType.OAuth2 && !string.IsNullOrWhiteSpace(c.OAuthAccessToken))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", c.OAuthAccessToken);
            }
        }

        public static void ApplyAuthHeaders(this ClientWebSocketOptions opt, McpConnectorConfig c)
        {
            if (c.Auth == AuthType.BearerOrApiKey && !string.IsNullOrWhiteSpace(c.AuthToken))
            {
                if (string.Equals(c.ApiKeyHeaderName, "Authorization", StringComparison.OrdinalIgnoreCase))
                    opt.SetRequestHeader("Authorization", $"Bearer {c.AuthToken}");
                else
                    opt.SetRequestHeader(c.ApiKeyHeaderName, c.AuthToken);
            }
            else if (c.Auth == AuthType.OAuth2 && !string.IsNullOrWhiteSpace(c.OAuthAccessToken))
            {
                opt.SetRequestHeader("Authorization", $"Bearer {c.OAuthAccessToken}");
            }
        }
        public static Dictionary<string, string>? BuildAuthHeaders(McpConnectorConfig cfg)
        {
            // Prefer OAuth access token if available
            var token = !string.IsNullOrWhiteSpace(cfg.OAuthAccessToken) ? cfg.OAuthAccessToken : cfg.AuthToken;

            if (string.IsNullOrWhiteSpace(token))
                return null;

            // Normalize header name (default Authorization)
            var headerName = string.IsNullOrWhiteSpace(cfg.ApiKeyHeaderName)
                ? "Authorization" : cfg.ApiKeyHeaderName;

            // If using Authorization, ensure single Bearer prefix
            if (headerName.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                var hasPrefix = token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                                || token.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)
                                || token.StartsWith("Digest ", StringComparison.OrdinalIgnoreCase)
                                || token.Contains('.'); // many JWTs lack prefix; treat as bare

                token = hasPrefix ? token : $"Bearer {token}";
            }

            return new() { [headerName] = token };
        }

    
    }
    internal static class WwwAuthHelper
    {
        public static (string? resourceMeta, string? scope) Parse(HttpResponseMessage r)
        {
            if (!r.Headers.TryGetValues("WWW-Authenticate", out var values))
                return (null, null);

            string? resourceMeta = null;
            string? scope = null;

            foreach (var header in values)
            {
                var h = header.Trim();
                if (!h.StartsWith("Bearer", StringComparison.OrdinalIgnoreCase))
                    continue;

                var paramStr = h.Substring("Bearer".Length).TrimStart();
                foreach (var (k, v) in ParseAuthParams(paramStr))
                {
                    if (k.Equals("resource_metadata", StringComparison.OrdinalIgnoreCase))
                        resourceMeta ??= v;
                    else if (k.Equals("scope", StringComparison.OrdinalIgnoreCase))
                        scope ??= v;
                }
            }
            return (resourceMeta, scope);

            static IEnumerable<(string key, string val)> ParseAuthParams(string s)
            {
                int i = 0, n = s.Length;
                while (i < n)
                {
                    while (i < n && (s[i] == ',' || char.IsWhiteSpace(s[i]))) i++;
                    int start = i;
                    while (i < n && s[i] != '=' && s[i] != ',') i++;
                    if (i >= n || s[i] != '=') yield break;
                    var key = s.Substring(start, i - start).Trim();
                    i++; // '='
                    string val;
                    if (i < n && s[i] == '"')
                    {
                        i++;
                        var sb = new System.Text.StringBuilder();
                        while (i < n)
                        {
                            if (s[i] == '\\' && i + 1 < n) { sb.Append(s[i + 1]); i += 2; continue; }
                            if (s[i] == '"') { i++; break; }
                            sb.Append(s[i]); i++;
                        }
                        val = sb.ToString();
                    }
                    else
                    {
                        int vstart = i;
                        while (i < n && s[i] != ',') i++;
                        val = s.Substring(vstart, i - vstart).Trim();
                    }
                    yield return (key, val);
                }
            }
        }
    }
}
