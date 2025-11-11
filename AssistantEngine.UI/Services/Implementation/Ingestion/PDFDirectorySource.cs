
using AssistantEngine.UI.Services.Implementation.Ingestion.Chunks;
using AssistantEngine.UI.Services.Models.Ingestion;
using AssistantEngine.UI.Services.Types;
using Microsoft.SemanticKernel.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace AssistantEngine.UI.Services.Implementation.Ingestion;

public class PDFDirectorySource(string sourceDirectory, bool includeSubdirectories) : IIngestionSource
{
   public event Action<string>? OnProgressMessage;
    private void ProgressMessage(string msg, StatusLevel statusLevel = StatusLevel.Information) => OnProgressMessage?.Invoke(msg);
    public string SourceFileId(string fullPath)
       => Path
           .GetRelativePath(sourceDirectory, fullPath)
           .Replace(Path.DirectorySeparatorChar, '/');
    public static string SourceFileVersion(string path) => File.GetLastWriteTimeUtc(path).ToString("o");

    public string SourceId => $"{nameof(PDFDirectorySource)}:{sourceDirectory}";

    public Task<IEnumerable<IngestedDocument>> GetNewOrModifiedDocumentsAsync(IReadOnlyList<IngestedDocument> existingDocuments)
    {
        var results = new List<IngestedDocument>();

        // 1) Build a deduplicated lookup of the existing documents by DocumentId
        var existingById = existingDocuments
            .GroupBy(d => d.DocumentId)
            .Select(g => g
                // if there were duplicates, pick the one with the latest version
                .OrderByDescending(d => d.DocumentVersion)
                .First()
            )
            .ToDictionary(d => d.DocumentId);

        // 2) Find all .pdf files under the source
        var sourceFiles = Directory.GetFiles(
            sourceDirectory,
            "*.pdf",
            includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

        foreach (var fullPath in sourceFiles)
        {
            var id = SourceFileId(fullPath);
            ProgressMessage($"Ingesting {id}");
            var version = SourceFileVersion(fullPath);

            if (existingById.TryGetValue(id, out var oldDoc))
            {
                // file already known
                if (oldDoc.DocumentVersion != version)
                {
                    // version changed → update in place
                    results.Add(new IngestedDocument
                    {
                        Key = oldDoc.Key,
                        SourceId = SourceId,
                        DocumentId = id,
                        DocumentVersion = version
                    });
                }
            }
            else
            {
                // brand-new file → new key
                results.Add(new IngestedDocument
                {
                    Key = Guid.CreateVersion7().ToString(),
                    SourceId = SourceId,
                    DocumentId = id,
                    DocumentVersion = version
                });
            }
        }

        return Task.FromResult((IEnumerable<IngestedDocument>)results);
    }


    public Task<IEnumerable<IngestedDocument>> GetDeletedDocumentsAsync(IReadOnlyList<IngestedDocument> existingDocuments)
    {
        var currentFiles = Directory.GetFiles(sourceDirectory, "*.pdf",
      includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
        var currentFileIds = currentFiles.ToLookup(SourceFileId);
        var deletedDocuments = existingDocuments.Where(d => !currentFileIds.Contains(d.DocumentId));
        foreach (var deleted in deletedDocuments)
        {
            ProgressMessage($"Deleted {deleted.DocumentId}");
        }
        return Task.FromResult(deletedDocuments);
    }

    public Task<IEnumerable<IIngestedChunk>> CreateChunksForDocumentAsync(IngestedDocument document)
    {
        using var pdf = PdfDocument.Open(Path.Combine(sourceDirectory, document.DocumentId));
        var paragraphs = pdf.GetPages().SelectMany(GetPageParagraphs).ToList();
        ProgressMessage($"Reading {document.DocumentId}");

        var chunks = paragraphs.Select(p => new IngestedTextChunk
        {
            Key = Guid.NewGuid().ToString(),
            DocumentId = document.DocumentId,
            PageNumber = p.PageNumber,
            Text = p.Text
        });

        return Task.FromResult(chunks.Cast<IIngestedChunk>());
    }

    private static IEnumerable<(int PageNumber, int IndexOnPage, string Text)> GetPageParagraphs(Page pdfPage)
    {
        var letters = pdfPage.Letters;
        var words = NearestNeighbourWordExtractor.Instance.GetWords(letters);
        var textBlocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
        var pageText = string.Join(Environment.NewLine + Environment.NewLine,
            textBlocks.Select(t => t.Text.ReplaceLineEndings(" ")));

#pragma warning disable SKEXP0050 // Type is for evaluation purposes only
        return TextChunker.SplitPlainTextParagraphs([pageText], 200)
            .Select((text, index) => (pdfPage.Number, index, text));
#pragma warning restore SKEXP0050 // Type is for evaluation purposes only
    }
}
