using System.ComponentModel;

namespace AssistantEngine.Services.Implementation.Tools
{
    public class SearchTool: ITool 
    {
        private readonly SemanticSearch _search;
        public SearchTool(SemanticSearch search) => _search = search;

        [Description("Searches for information in text files using a phrase or keyword")]
        public async Task<IEnumerable<string>> SearchAsync(
        [Description("The phrase to search for.")]
        string searchPhrase,
        [Description("If not provided, searches all files.")]
        string? filenameFilter = null)
        {
            var filters = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(filenameFilter))
                filters["DocumentId"] = filenameFilter;

            var results = await _search.SearchAsync("text-chunks", searchPhrase, filters, maxResults: 5);
            return results.Select(r
                => $"<result filename=\"{r.DocumentId}\" page_number=\"1\">{r.Text}</result>");
        }


    }
}
