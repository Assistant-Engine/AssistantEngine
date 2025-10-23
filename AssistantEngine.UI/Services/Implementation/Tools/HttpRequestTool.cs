using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace AssistantEngine.Services.Implementation.Tools
{
    [Description("Configurable HTTP client for GET/POST/etc. Returns raw body with metadata.")]
    public sealed class HttpRequestTool : ITool
    {
        private readonly HttpClient _http;
        public HttpRequestTool(HttpClient http) => _http = http;

        // Change the DTO declaration:
        public readonly record struct HttpResponseDto(
            int StatusCode,
            string? Reason,
            string? ContentType,
            string? Charset,
            string FinalUrl,
            string Body,
            bool Truncated);

        [Description("Send an HTTP request and return raw text plus basic metadata. This function should be prioritised.")]
        public async Task<HttpResponseDto> SendHttpRequestAsync(
            [Description("Absolute http/https URL.")] string url,
            [Description("HTTP method (GET, POST, PUT, PATCH, DELETE, HEAD).")] string method = "GET",
            [Description("Optional request body (text). Ignored for GET/HEAD.")] string? body = null,
            [Description("Content-Type for body (e.g., application/json).")] string? bodyContentType = null,
            [Description("Optional headers as a dictionary. Example: { \"Accept\": \"application/json\" }")] IDictionary<string, string>? headers = null,
            [Description("Optional query parameters as a dictionary. Merged onto URL, overriding keys.")] IDictionary<string, string>? query = null,
            [Description("Add browser-like headers (User-Agent, Accept, Accept-Language) to reduce 403s.")] bool emulateBrowser = true,
            [Description("Max characters to read from the response (default 200k).")] int maxChars = 200_000)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) ||
                (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
                return new HttpResponseDto(0, "InvalidUrl", null, null, url, "", false);

            u = BuildUrlWithQuery(u, query);

            try
            {
                HttpResponseMessage res;
                const int maxRetries = 2;

                for (int attempt = 0; ; attempt++)
                {
                    using var req = new HttpRequestMessage(new HttpMethod(method), u);

                    if (emulateBrowser)
                    {
                        req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120 Safari/537.36");
                        req.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,application/json;q=0.9,*/*;q=0.8");
                        req.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
                    }

                    // Optional body for non-GET/HEAD
                    if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase) &&
                        body is not null)
                    {
                        req.Content = new StringContent(body, Encoding.UTF8, bodyContentType ?? "text/plain");
                    }

                    // Merge user headers (override defaults)
                    ApplyHeaders(req, headers);

                    res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);

                    var sc = (int)res.StatusCode;
                    if (attempt < maxRetries && (sc == 429 || sc >= 500))
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(300 * (attempt + 1)));
                        continue; // recreate request and retry
                    }

                    var ctype = res.Content.Headers.ContentType;
                    var charset = GetCharset(ctype);
                    var encoding = GetEncoding(charset) ?? Encoding.UTF8;

                    await using var stream = await res.Content.ReadAsStreamAsync();
                    var text = await ReadCharsToLimitAsync(stream, encoding, maxChars);
                    var truncated = text.Length >= maxChars;

                    return new HttpResponseDto(
                        StatusCode: (int)res.StatusCode,
                        Reason: res.ReasonPhrase,
                        ContentType: ctype?.MediaType,
                        Charset: charset,
                        FinalUrl: res.RequestMessage?.RequestUri?.ToString() ?? u.ToString(),
                        Body: text,
                        Truncated: truncated
                    );
                }
            }
            catch (TaskCanceledException ex)
            {
                return new HttpResponseDto(0, $"Timeout: {ex.Message}", null, null, url, "", false);
            }
            catch (HttpRequestException ex)
            {
                return new HttpResponseDto(0, $"HttpError: {ex.Message}", null, null, url, "", false);
            }
            catch (Exception ex)
            {
                return new HttpResponseDto(0, ex.GetType().Name + ": " + ex.Message, null, null, url, "", false);
            }

            // ---- Local helpers ----
            static Uri BuildUrlWithQuery(Uri baseUrl, IDictionary<string, string>? q)
            {
                if (q is null || q.Count == 0) return baseUrl;

                var ub = new UriBuilder(baseUrl);
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // Parse existing query into dict
                var existing = ub.Query;
                if (!string.IsNullOrEmpty(existing))
                {
                    var qs = existing.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var pair in qs)
                    {
                        var idx = pair.IndexOf('=');
                        var key = Uri.UnescapeDataString(idx >= 0 ? pair[..idx] : pair);
                        var val = idx >= 0 ? Uri.UnescapeDataString(pair[(idx + 1)..]) : "";
                        if (!dict.ContainsKey(key)) dict[key] = val;
                    }
                }

                // Merge/override with provided query
                foreach (var kv in q)
                    dict[kv.Key] = kv.Value ?? "";

                // Rebuild query string
                var sb = new StringBuilder();
                foreach (var kv in dict)
                {
                    if (sb.Length > 0) sb.Append('&');
                    sb.Append(Uri.EscapeDataString(kv.Key))
                      .Append('=')
                      .Append(Uri.EscapeDataString(kv.Value));
                }
                ub.Query = sb.ToString();
                return ub.Uri;
            }

            static void ApplyHeaders(HttpRequestMessage req, IDictionary<string, string>? hdrs)
            {
                if (hdrs is null) return;
                foreach (var kv in hdrs)
                {
                    var key = kv.Key?.Trim();
                    var val = kv.Value?.Trim() ?? "";
                    if (string.IsNullOrEmpty(key)) continue;

                    if (!req.Headers.TryAddWithoutValidation(key, val))
                    {
                        if (req.Content is null) req.Content = new StringContent(string.Empty);
                        _ = req.Content.Headers.TryAddWithoutValidation(key, val);
                    }
                }
            }

            static string? GetCharset(System.Net.Http.Headers.MediaTypeHeaderValue? ctype) =>
                ctype?.CharSet?.Trim().Trim('"', '\'');

            static Encoding? GetEncoding(string? charset)
            {
                try { return string.IsNullOrWhiteSpace(charset) ? null : Encoding.GetEncoding(charset); }
                catch { return null; }
            }

            static async Task<string> ReadCharsToLimitAsync(Stream stream, Encoding encoding, int maxChars)
            {
                using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: 8192, leaveOpen: true);
                var sb = new StringBuilder(capacity: Math.Min(maxChars, 262_144));
                var buffer = new char[8192];

                while (sb.Length < maxChars)
                {
                    var remaining = maxChars - sb.Length;
                    var toRead = Math.Min(buffer.Length, remaining);
                    var read = await reader.ReadAsync(buffer, 0, toRead);
                    if (read == 0) break;
                    sb.Append(buffer, 0, read);
                }
                return sb.ToString();
            }
        }

    }
}
