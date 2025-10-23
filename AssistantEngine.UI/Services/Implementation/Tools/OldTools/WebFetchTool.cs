using AssistantEngine.Services.Implementation.Tools;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.Security;
using System.Text;

namespace AssistantEngine.UI.Services.Implementation.Tools.OldTools
{
    public sealed class WebFetchTool : ITool
    {
        private readonly HttpClient _http;
        public WebFetchTool(HttpClient http) => _http = http;

  
        [Description("Fetch a web page and return its raw HTML wrapped in XML")]
        public async Task<string> FetchHtmlAsync(
    [Description("Absolute http/https URL to fetch.")] string url,
    [Description("Max characters to return (default 10k).")] int maxChars = 10000)

        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) ||
                u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)
                return "<error type=\"InvalidUrl\" message=\"Only absolute http/https URLs allowed.\" />";

            try
            {
                using var res = await _http.GetAsync(u, HttpCompletionOption.ResponseHeadersRead);
                res.EnsureSuccessStatusCode();

                // Content-type charset (fallback UTF-8)
                var charset = GetCharset(res.Content.Headers.ContentType);
                var encoding = GetEncoding(charset) ?? Encoding.UTF8;

                await using var stream = await res.Content.ReadAsStreamAsync();
                var html = await ReadCharsToLimitAsync(stream, encoding, maxChars);

                return $@"<page url=""{SecurityElement.Escape(u.ToString())}""><html><![CDATA[{html}]]></html></page>";
            }
            catch (TaskCanceledException ex)
            {
                return $@"<error type=""Timeout"" message=""{SecurityElement.Escape(ex.Message)}"" />";
            }
            catch (HttpRequestException ex)
            {
                return $@"<error type=""HttpRequestException"" message=""{SecurityElement.Escape(ex.Message)}"" />";
            }
            catch (Exception ex)
            {
                return $@"<error type=""{ex.GetType().Name}"" message=""{SecurityElement.Escape(ex.Message)}"" />";
            }
        }

        static string? GetCharset(MediaTypeHeaderValue? ctype) =>
            ctype?.CharSet?.Trim().Trim('"', '\'');

        static Encoding? GetEncoding(string? charset)
        {
            try { return string.IsNullOrWhiteSpace(charset) ? null : Encoding.GetEncoding(charset); }
            catch { return null; }
        }

        static async Task<string> ReadToByteLimitAsync(Stream s, Encoding enc, int maxBytes)
        {
            var buffer = new byte[8192];
            var total = 0;
            using var ms = new MemoryStream(capacity: Math.Min(maxBytes, 1_572_864)); // ~1.5MB+
            int read;
            while ((read = await s.ReadAsync(buffer, 0, Math.Min(buffer.Length, maxBytes - total))) > 0)
            {
                await ms.WriteAsync(buffer, 0, read);
                total += read;
                if (total >= maxBytes) break;
            }
            return enc.GetString(ms.ToArray());
        }
        static async Task<string> ReadCharsToLimitAsync(Stream stream, Encoding encoding, int maxChars)
        {
            // StreamReader decodes incrementally; we stop exactly at maxChars without over-reading.
            using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: 8192, leaveOpen: true);
            var sb = new StringBuilder(capacity: Math.Min(maxChars, 262_144)); // pre-allocate up to 256k
            var buffer = new char[8192];

            while (sb.Length < maxChars)
            {
                var remaining = maxChars - sb.Length;
                var toRead = Math.Min(buffer.Length, remaining);
                var read = await reader.ReadAsync(buffer, 0, toRead);
                if (read == 0) break; // EOF
                sb.Append(buffer, 0, read);
            }

            return sb.ToString();
        }

    }
}
