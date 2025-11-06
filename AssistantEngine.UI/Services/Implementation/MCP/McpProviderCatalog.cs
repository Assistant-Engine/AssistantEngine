// NEW: enum used in catalog & UI
using AssistantEngine.UI.Services.Models;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace AssistantEngine.UI.Services.Implementation.MCP
{
    public enum ClientMode
    {
        PlatformManagedConfidential,
        UserSuppliedConfidential,
        PublicPkce,
        DeviceCode
    }

    // UPDATED: provider record with optional fields
    public record McpProviderItem(
        string key,
        string name,
        string category,
        string url,
        string authentication,
        string maintainer,
        string logo,
        // additions (nullable)
        string? clientMode = null,
        string? authUrl = null,
        string? tokenUrl = null,
        string? scopes = null,
        bool? supportsApiKey = null,
        string? defaultClientId = null
    );

    public interface IMcpProviderCatalog
    {
        Task<IReadOnlyList<McpProviderItem>> GetAllAsync();
        Task<(string authUrl, string tokenUrl)?> DiscoverOidcAsync(string baseUrl);
        Task<(string? resourceMeta, string? issuer)> DiscoverProtectedResourceAsync(string serverUrl);
        Task<(string? auth, string? token, string? register, string[]? scopes)> DiscoverAuthorizationServerAsync(string issuer);
    }

    public sealed class McpProviderCatalog(HttpClient http, IConfiguration cfg) : IMcpProviderCatalog
    {
        // McpProviderCatalog.cs (or a new helper service)
        public async Task<(string? resourceMeta, string? issuer)> DiscoverProtectedResourceAsync(string serverUrl)
        {
            var baseUri = new Uri(serverUrl);
            var origin = baseUri.GetLeftPart(UriPartial.Authority);
            // Try /.well-known oauth-protected-resource at path, then root
            var candidates = new[]
            {
        // path-specific
        $"{origin}/.well-known/oauth-protected-resource{baseUri.AbsolutePath}".TrimEnd('/'),
        // root
        $"{origin}/.well-known/oauth-protected-resource"
    }.Distinct().ToArray();

            foreach (var url in candidates)
            {
                try
                {
                    using var r = await http.GetAsync(url);
                    if (!r.IsSuccessStatusCode) continue;
                    using var doc = await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync());
                    var root = doc.RootElement;

                    // RFC9728 must include "authorization_servers" (array of issuers)
                    if (root.TryGetProperty("authorization_servers", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        var firstIssuer = arr.EnumerateArray().FirstOrDefault().GetString();
                        if (!string.IsNullOrWhiteSpace(firstIssuer))
                            return (url, firstIssuer);
                    }
                }
                catch { /* ignore */ }
            }
            return (null, null);
        }

        // Full AS metadata discovery per spec (path-insertion priority)
        public async Task<(string? auth, string? token, string? register, string[]? scopes)> DiscoverAuthorizationServerAsync(string issuer)
        {
            static string TrimSlash(string s) => s.TrimEnd('/');
            var i = TrimSlash(issuer);
            var (host, path) = (new Uri(i).GetLeftPart(UriPartial.Authority), new Uri(i).AbsolutePath.Trim('/'));

            var endpoints = new List<string>();
            if (!string.IsNullOrEmpty(path))
            {
                endpoints.Add($"{host}/.well-known/oauth-authorization-server/{path}");
                endpoints.Add($"{host}/.well-known/openid-configuration/{path}");
                endpoints.Add($"{i}/.well-known/openid-configuration");
            }
            else
            {
                endpoints.Add($"{i}/.well-known/oauth-authorization-server");
                endpoints.Add($"{i}/.well-known/openid-configuration");
            }

            foreach (var url in endpoints.Distinct())
            {
                try
                {
                    using var r = await http.GetAsync(url);
                    if (!r.IsSuccessStatusCode) continue;
                    using var doc = await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync());
                    var root = doc.RootElement;

                    string? auth = root.TryGetProperty("authorization_endpoint", out var a) ? a.GetString() : null;
                    string? tok = root.TryGetProperty("token_endpoint", out var t) ? t.GetString() : null;
                    string? reg = root.TryGetProperty("registration_endpoint", out var g) ? g.GetString() : null;

                    string[]? scopes = null;
                    if (root.TryGetProperty("scopes_supported", out var s) && s.ValueKind == JsonValueKind.Array)
                        scopes = s.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()!;

                    if (!string.IsNullOrWhiteSpace(auth) && !string.IsNullOrWhiteSpace(tok))
                        return (auth, tok, reg, scopes);
                }
                catch { /* ignore */ }
            }
            return (null, null, null, null);
        }


        private IReadOnlyList<McpProviderItem>? _cache;

        public async Task<IReadOnlyList<McpProviderItem>> GetAllAsync()
        {
            if (_cache is not null) return _cache;

            var fromConfig = cfg.GetSection("McpProviders").Get<List<McpProviderItem>>();
            if (fromConfig is { Count: > 0 })
                return _cache = fromConfig;

            var asm = typeof(McpProviderCatalog).Assembly;
            var resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("Config.mcp-providers.json", StringComparison.OrdinalIgnoreCase));

            if (resName is null) return _cache = Array.Empty<McpProviderItem>();

            using var s = asm.GetManifestResourceStream(resName)!;
            _cache = await System.Text.Json.JsonSerializer
                       .DeserializeAsync<IReadOnlyList<McpProviderItem>>(s)
                     ?? Array.Empty<McpProviderItem>();

            return _cache;
        }

        public async Task<(string authUrl, string tokenUrl)?> DiscoverOidcAsync(string baseUrl)
        {
            var host = new Uri(baseUrl).GetLeftPart(UriPartial.Authority);
            foreach (var u in new[]
            {
            $"{host}/.well-known/openid-configuration",
            $"{host}/.well-known/oauth-authorization-server"
        })
            {
                try
                {
                    using var resp = await http.GetAsync(u);
                    if (!resp.IsSuccessStatusCode) continue;
                    using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStreamAsync());
                    var root = doc.RootElement;

                    var auth = root.TryGetProperty("authorization_endpoint", out var a) && a.ValueKind == JsonValueKind.String
                        ? a.GetString() : null;
                    var token = root.TryGetProperty("token_endpoint", out var t) && t.ValueKind == JsonValueKind.String
                        ? t.GetString() : null;

                    if (!string.IsNullOrWhiteSpace(auth) && !string.IsNullOrWhiteSpace(token))
                        return (auth!, token!);
                }
                catch { /* ignore */ }
            }
            return null;
        }
    }

    internal static class McpProviderUtil
    {
        public static AuthType ResolveAuth(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return AuthType.None;
            var x = s.Trim().ToLowerInvariant();
            if (x.Contains("oauth")) return AuthType.OAuth2;
            if (x.Contains("bearer") || x.Contains("api")) return AuthType.BearerOrApiKey;
            return AuthType.None;
        }

        public static bool IsCustom(this McpConnectorConfig c)
            => string.Equals(c.ProviderKey, "custom", StringComparison.OrdinalIgnoreCase);

        public static void ClearOAuth(this McpConnectorConfig c)
        {
            c.OAuthAccessToken = null;
            c.OAuthRefreshToken = null;
            c.OAuthExpiryUtc = null;
            c.OAuthAuthUrl = null;
            c.OAuthTokenUrl = null;
            c.OAuthScopes = null;
        }

        public static void ClearApiKey(this McpConnectorConfig c)
        {
            c.AuthToken = null;
            if (string.IsNullOrWhiteSpace(c.ApiKeyHeaderName))
                c.ApiKeyHeaderName = "Authorization";
        }

        // UPDATED: prefill uses clientMode & defaults
        public static void PrefillFromProvider(this McpConnectorConfig conn, McpProviderItem p)
        {
            conn.ProviderKey = p.key;
            conn.ProviderLogo = p.logo;

            if (conn.IsCustom())
                return;

            conn.Id = p.name;
            conn.ServerUrl = p.url;
            conn.Auth = ResolveAuth(p.authentication);

            // new: map clientMode from provider → connector (nullable-safe)
            conn.ClientMode = p.clientMode?.ToLowerInvariant() switch
            {
                "platform_managed_confidential" => ClientMode.PlatformManagedConfidential,
                "user_supplied_confidential" => ClientMode.UserSuppliedConfidential,
                "public_pkce" => ClientMode.PublicPkce,
                "device_code" => ClientMode.DeviceCode,
                _ => ClientMode.PublicPkce // sensible default
            };

            // optional hints
            conn.SupportsApiKey = p.supportsApiKey ?? false;

            if (conn.Auth == AuthType.OAuth2)
            {
                conn.ClearApiKey();

                // prefer explicit URLs if present
                conn.OAuthAuthUrl = p.authUrl ?? conn.OAuthAuthUrl;
                conn.OAuthTokenUrl = p.tokenUrl ?? conn.OAuthTokenUrl;

                // default scopes only if user hasn't overridden
                if (string.IsNullOrWhiteSpace(conn.OAuthScopes) && !string.IsNullOrWhiteSpace(p.scopes))
                    conn.OAuthScopes = p.scopes;

                // public PKCE default client id
                if (conn.ClientMode == ClientMode.PublicPkce && string.IsNullOrWhiteSpace(conn.OAuthClientId))
                    conn.OAuthClientId = p.defaultClientId;
            }
            else if (conn.Auth == AuthType.BearerOrApiKey)
            {
                conn.ClearOAuth();
            }
            else
            {
                conn.ClearOAuth();
                conn.ClearApiKey();
            }
        }
    }
}