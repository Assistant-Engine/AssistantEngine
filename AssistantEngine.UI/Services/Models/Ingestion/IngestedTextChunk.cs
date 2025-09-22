using Microsoft.Extensions.VectorData;

namespace AssistantEngine.UI.Services.Models.Ingestion;

public class IngestedTextChunk: IIngestedChunk
{
    private const int VectorDimensions = 384; // 384 is the default vector size for the all-minilm embedding model
    private const string VectorDistanceFunction = DistanceFunction.CosineDistance;

    [VectorStoreKey]
    public required string Key { get; set; }

    [VectorStoreData(IsIndexed = true)]
    public required string DocumentId { get; set; }

    [VectorStoreData]
    public int PageNumber { get; set; }

    [VectorStoreData]
    public required string Text { get; set; }

    [VectorStoreVector(VectorDimensions, DistanceFunction = VectorDistanceFunction)]
    public string? Vector => Text;
}
