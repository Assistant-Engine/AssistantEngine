using Microsoft.Extensions.VectorData;

namespace AssistantEngine.UI.Services.Models.Ingestion;

using Microsoft.Extensions.VectorData;



public class IngestedCodeChunk: IIngestedChunk
{
    private const int VectorDimensions = 384;
    private const string VectorDistanceFunction = DistanceFunction.CosineDistance;

    [VectorStoreKey]
    public string Key { get; set; } = Guid.NewGuid().ToString();

    [VectorStoreData]
    public string Text { get; set; }
    [VectorStoreData(IsIndexed = true)]
    public string DocumentId { get; set; }

    [VectorStoreData(IsIndexed = true)]
    public string Type { get; set; }

    [VectorStoreData(IsIndexed = true)]
    public string Name { get; set; }

    [VectorStoreData(IsIndexed = true)]
    public string Namespace { get; set; }

    [VectorStoreData(IsIndexed = true)]
    public string ParentClass { get; set; }

    [VectorStoreData]
    public string Parameters { get; set; }

    [VectorStoreData]
    public string ReturnType { get; set; }

    [VectorStoreData]
    public string Attributes { get; set; }

    [VectorStoreData]
    public string XmlDocs { get; set; }

    [VectorStoreData]
    public int StartLine { get; set; }

    [VectorStoreData]
    public int EndLine { get; set; }

    [VectorStoreData(IsIndexed = true)]
    public string FilePath { get; set; }

    [VectorStoreVector(VectorDimensions, DistanceFunction = VectorDistanceFunction)]
    public string? Vector => Text;
}

