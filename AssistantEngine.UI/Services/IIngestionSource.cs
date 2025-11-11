using AssistantEngine.UI.Services.Models.Ingestion;
using AssistantEngine.UI.Services.Types;

namespace AssistantEngine.UI.Services;

public interface IIngestionSource
{
    public event Action<string>? OnProgressMessage;
    string SourceId { get; }

    Task<IEnumerable<IngestedDocument>> GetNewOrModifiedDocumentsAsync(IReadOnlyList<IngestedDocument> existingDocuments);

    Task<IEnumerable<IngestedDocument>> GetDeletedDocumentsAsync(IReadOnlyList<IngestedDocument> existingDocuments);

    Task<IEnumerable<IIngestedChunk>> CreateChunksForDocumentAsync(IngestedDocument document);  //any additional descriptive process goes here
}
