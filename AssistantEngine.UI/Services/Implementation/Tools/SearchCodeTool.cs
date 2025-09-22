using System.ComponentModel;

namespace AssistantEngine.Services.Implementation.Tools
{
    public class SearchCodeTool: ITool
    {
        private readonly SemanticSearch _search;
        public SearchCodeTool(SemanticSearch search) => _search = search;

        [Description("Searches for information in csharp code files. For other code files use search tool")]
        public async Task<IEnumerable<string>> SearchCodeAsync(
        [Description("The text to search for.")] string searchPhrase,
        [Description("If set, only search in that filename.")] string? filenameFilter = null,
        [Description("If set, only search chunks of this type (e.g. Method, Class).")] string? typeFilter = null,
        [Description("If set, only search chunks with this name.")] string? nameFilter = null,
        [Description("If set, only search code chunks in that namespace.")] string? namespaceFilter = null
    )
        {
            var filters = new Dictionary<string, string>();
            //if (!string.IsNullOrEmpty(filenameFilter))
                //filters["DocumentId"] = filenameFilter; //temporarily comment this as it onlt contained .cs
        ///   await InvokeAsync(StateHasChanged);
            var results = await _search.SearchAsync("code-chunks", searchPhrase, filters, maxResults: 10);// temporarily changing max results to 10
            return results.Select(result =>
                $"<result filename=\"{result.DocumentId}\" page_number=\"1\">{result.Text}</result>");
        }
    }
}
