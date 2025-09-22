// SemanticSearch.cs

using AssistantEngine.UI.Services.Implementation.Ingestion.Chunks;
using AssistantEngine.UI.Services.Models.Ingestion;
using Microsoft.Extensions.VectorData;

public class SemanticSearch
{
    private readonly IChunkStoreFactory _stores;
    public SemanticSearch(IChunkStoreFactory stores)
    {
        _stores = stores;
    }

    public Task<IReadOnlyList<IIngestedChunk>> SearchAsync(
        string storeName,
        string query,
        IDictionary<string, string> metadataFilters,
        int maxResults = 5)
    {
        var store = _stores.Get(storeName);
        return store.SearchAsync(query, maxResults, metadataFilters);
    }

    // Optional generic overload if you want typed chunks back
    public async Task<IReadOnlyList<TChunk>> SearchAsync<TChunk>(
        string storeName,
        string query,
        IDictionary<string, string> metadataFilters,
        int maxResults = 5
    ) where TChunk : IIngestedChunk
    {
        var raw = await SearchAsync(storeName, query, metadataFilters, maxResults);
        return raw.Cast<TChunk>().ToList();
    }
}
