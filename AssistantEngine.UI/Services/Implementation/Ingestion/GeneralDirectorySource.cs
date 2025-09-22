
using AssistantEngine.UI.Services.Implementation.Ingestion.Chunks;
using AssistantEngine.UI.Services.Models.Ingestion;
using Microsoft.SemanticKernel.Text;
using System.Text;
using System.Text.RegularExpressions;

namespace AssistantEngine.UI.Services.Implementation.Ingestion;

public sealed class GeneralDirectorySource : IIngestionSource
{
    public event Action<string>? StatusMessage;
    private void OnStatus(string msg) => StatusMessage?.Invoke(msg);

    private readonly string _sourceDirectory;
    private readonly bool _includeSubdirectories;
    private readonly HashSet<string> _extensions;

    // Default set of "text-like" extensions we will ingest as plain text.
    // (PDFs still go through PDFDirectorySource for better layout extraction.)
    private static readonly string[] DefaultExtensions = new[]
    {
        ".txt", ".md", ".markdown",
        ".json", ".yaml", ".yml",
        ".xml", ".html", ".htm",
        ".css", ".js", ".ts", ".tsx",
        ".cshtml", ".razor",
        ".sql", ".csv", ".ini", ".config",
        ".csproj", ".sln", ".props", ".targets",
        ".bat", ".ps1", ".sh",
        ".py", ".java", ".kt", ".go", ".rs", ".swift", ".vb", ".php"
        // (intentionally excluding .pdf — handled by PDFDirectorySource)
        // (intentionally excluding binary types: images, fonts, office binaries, etc.)
    };
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
{ ".cs", ".pdf" }; // handled by dedicated sources
    public GeneralDirectorySource(
        string sourceDirectory,
        bool includeSubdirectories,
        IEnumerable<string>? extensions = null)
    {
        _sourceDirectory = sourceDirectory;
        _includeSubdirectories = includeSubdirectories;
        var incoming = (extensions ?? DefaultExtensions);
        var normalized = incoming
            .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
            .Where(e => !Excluded.Contains(e)); // drop .cs/.pdf even if user supplied
        _extensions = new HashSet<string>(normalized);
    }

    public string SourceId => $"{nameof(GeneralDirectorySource)}:{_sourceDirectory}";

    public static string SourceFileVersion(string path) => File.GetLastWriteTimeUtc(path).ToString("o");

    private string SourceFileId(string fullPath)
        => Path.GetRelativePath(_sourceDirectory, fullPath)
               .Replace(Path.DirectorySeparatorChar, '/');

    private IEnumerable<string> EnumerateFiles()
    {
        if (_extensions.Count == 0) yield break;
        var opt = _includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        // Enumerate all, then filter by extension; avoids multiple FS passes.
        foreach (var path in Directory.EnumerateFiles(_sourceDirectory, "*", opt))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (_extensions.Contains(ext))
                yield return path;
        }
    }

    public Task<IEnumerable<IngestedDocument>> GetNewOrModifiedDocumentsAsync(IReadOnlyList<IngestedDocument> existingDocuments)
    {
        var results = new List<IngestedDocument>();

        // Deduplicate existing by DocumentId (keep the latest version)
        var existingById = existingDocuments
            .GroupBy(d => d.DocumentId)
            .Select(g => g.OrderByDescending(d => d.DocumentVersion).First())
            .ToDictionary(d => d.DocumentId);

        foreach (var fullPath in EnumerateFiles())
        {
            var id = SourceFileId(fullPath);
            var version = SourceFileVersion(fullPath);
            OnStatus($"Ingesting {id}");

            if (existingById.TryGetValue(id, out var oldDoc))
            {
                if (oldDoc.DocumentVersion != version)
                {
                    // Update in place
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
                // Brand new
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
        var current = EnumerateFiles().ToLookup(SourceFileId);
        var deleted = existingDocuments.Where(d => !current.Contains(d.DocumentId)).ToList();
        foreach (var d in deleted)
            OnStatus($"Deleted {d.DocumentId}");
        return Task.FromResult((IEnumerable<IngestedDocument>)deleted);
    }

    public async Task<IEnumerable<IIngestedChunk>> CreateChunksForDocumentAsync(IngestedDocument document)
    {
        var fullPath = Path.Combine(_sourceDirectory, document.DocumentId);
        OnStatus($"Reading {document.DocumentId}");

        // Read as UTF-8 (with BOM support); if fails, fall back to default encoding.
        string raw;
        try
        {
            raw = await File.ReadAllTextAsync(fullPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false));
        }
        catch
        {
            raw = await File.ReadAllTextAsync(fullPath);
        }

        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        var text = Normalize(ext, raw);

#pragma warning disable SKEXP0050 // Type is for evaluation purposes only
        var parts = TextChunker.SplitPlainTextParagraphs([text], 300).ToList();
#pragma warning restore SKEXP0050

        var chunks = parts.Select((t, i) => new IngestedTextChunk
        {
            Key = Guid.NewGuid().ToString(),
            DocumentId = document.DocumentId,
            PageNumber = i + 1, // pseudo page index for non-PDFs
            Text = t
        });

        return chunks.Cast<IIngestedChunk>();
    }

    // Minimal normalization: normalize line endings; optionally strip HTML tags for .html/.htm
    private static string Normalize(string ext, string input)
    {
        var s = input.Replace("\r\n", "\n").Replace("\r", "\n");

        if (ext is ".html" or ".htm")
        {
            // naive tag strip keeps text searchable; avoids pulling in extra deps
            s = Regex.Replace(s, "<script[\\s\\S]*?</script>", string.Empty, RegexOptions.IgnoreCase);
            s = Regex.Replace(s, "<style[\\s\\S]*?</style>", string.Empty, RegexOptions.IgnoreCase);
            s = Regex.Replace(s, "<[^>]+>", " ");
        }

        // Collapse excessive whitespace
        s = Regex.Replace(s, "[ \\t\\u00A0]+", " ");
        s = Regex.Replace(s, "\\n{3,}", "\n\n");

        return s.Trim();
    }
}
