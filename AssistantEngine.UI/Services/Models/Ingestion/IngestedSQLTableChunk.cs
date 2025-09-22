using Microsoft.Extensions.VectorData;

namespace AssistantEngine.UI.Services.Models.Ingestion;

using Microsoft.Extensions.VectorData;



public class IngestedSQLTableChunk : IIngestedChunk
{
    private const int VectorDimensions = 384;
    private const string VectorDistanceFunction = DistanceFunction.CosineDistance;

    [VectorStoreKey]
    public string Key { get; set; } = Guid.NewGuid().ToString();

    [VectorStoreData]
    public string Text { get; set; }
    
    [VectorStoreData(IsIndexed = true)]
    public string DocumentId { get; set; }

    [VectorStoreData]
    public string DatabaseId { get; set; }

    [VectorStoreData]
    public string DatabaseName { get; set; }

    [VectorStoreData(IsIndexed = true)]
    public string TableName { get; set; }

    [VectorStoreData]
    public string Fields { get; set; }

    [VectorStoreData]
    public string FieldDataTypes { get; set; }

    [VectorStoreData(IsIndexed = true)]
    public string ExampleQuery { get; set; }

    [VectorStoreVector(VectorDimensions, DistanceFunction = VectorDistanceFunction)]
    public string? Vector => Text;


}

