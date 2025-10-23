using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Security;
using System.Threading.Tasks;
using System.Xml;

namespace AssistantEngine.Services.Implementation.Tools
{
    public class WebSearchTool : ITool
    {
        private readonly HttpClient _http;

        // HttpClient injected via DI; configure timeouts/retries as you like
        public WebSearchTool(HttpClient http) => _http = http;

        [Description("Performs a web search via DuckDuckGo and returns the top results as XML snippets")]
        public async Task<IEnumerable<string>> WebSearchAsync(
            [Description("The query to search for on the web.")] string query,
            [Description("Maximum number of results to return (default is 5).")] int maxResults = 5)
        {
            try
            {
                var url = "https://html.duckduckgo.com/html?q="
                          + Uri.EscapeDataString(query);
                var html = await _http.GetStringAsync(url);

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var nodes = doc.DocumentNode
                    .SelectNodes("//a[@class='result__a']")
                    ?.Take(maxResults)
                    ?? Enumerable.Empty<HtmlNode>();
                IEnumerable<string> results = nodes.Select(n =>
                {
                    var title = SecurityElement.Escape(n.InnerText.Trim());
                    var href = n.GetAttributeValue("href", "");
                    return $"<result title=\"{title}\" url=\"{SecurityElement.Escape(href)}\" />";
                });
                return results;
            }
            catch (HttpRequestException ex)
            {
                return new[]
                {
                    $"<error type=\"HttpRequestException\" message=\"{SecurityElement.Escape(ex.Message)}\" />"
                };
            }
            catch (XmlException ex)
            {
                return new[]
                {
                    $"<error type=\"XmlException\" message=\"{SecurityElement.Escape(ex.Message)}\" />"
                };
            }
            catch (Exception ex)
            {
                return new[]
                {
                    $"<error type=\"{ex.GetType().Name}\" message=\"{SecurityElement.Escape(ex.Message)}\" />"
                };
            }
        }
    }
}
