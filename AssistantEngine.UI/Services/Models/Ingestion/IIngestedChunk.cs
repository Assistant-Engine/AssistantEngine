namespace AssistantEngine.UI.Services.Models.Ingestion
{
    public interface IIngestedChunk
    {
        string Key { get; }
        string Text { get; }
        string DocumentId { get; }
    }
}
