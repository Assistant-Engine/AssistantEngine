using AssistantEngine.UI.Services.Models.Ingestion;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.SemanticKernel.Text;
using System.Reflection.Metadata;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace AssistantEngine.UI.Services.Implementation.Ingestion;

public class CSDirectorySource(string sourceDirectory, bool includeSubdirectories) : IIngestionSource
{
    public event Action<string>? StatusMessage;
    private void OnStatus(string msg) => StatusMessage?.Invoke(msg);
    public string SourceFileId(string fullPath)
       => Path
           .GetRelativePath(sourceDirectory, fullPath)
           .Replace(Path.DirectorySeparatorChar, '/');
    public static string SourceFileVersion(string path) => File.GetLastWriteTimeUtc(path).ToString("o");

    public string SourceId => $"{nameof(CSDirectorySource)}:{sourceDirectory}";

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

        // 2) Find all .cs files under the source
        var sourceFiles = Directory.GetFiles(
            sourceDirectory,
            "*.cs",
            includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

        foreach (var fullPath in sourceFiles)
        {
            var id = SourceFileId(fullPath);
            OnStatus($"Ingesting {id}");
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

        var currentFiles = Directory.GetFiles(
    sourceDirectory,
    "*.cs",
    includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
        var currentFileIds = currentFiles.ToLookup(SourceFileId);

        var deletedDocuments = existingDocuments.Where(d => !currentFileIds.Contains(d.DocumentId));
        foreach (var deleted in deletedDocuments)
        {
            OnStatus($"Deleted {deleted.DocumentId}");
        }
        return Task.FromResult(deletedDocuments);
    }

    public async Task<IEnumerable<IIngestedChunk>> CreateChunksForDocumentAsync(IngestedDocument document)
    {
        
        var filePath = Path.Combine(sourceDirectory, document.DocumentId);
        OnStatus($"Reading {document.DocumentId}");
        var codeText = await File.ReadAllTextAsync(filePath);
        return GetEnhancedChunks(codeText, documentId: document.DocumentId, filePath: filePath);

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

    public static List<IngestedCodeChunk> GetEnhancedChunks(string code, string documentId, string filePath = "")
    {
        var chunks = new List<IngestedCodeChunk>();
        try
        {
            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetRoot();


            // Helper function to get XML docs
            string GetXmlDocs(SyntaxNode node) => node.GetLeadingTrivia()
                .Where(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia))
                .Select(t => t.ToFullString().Trim())
                .FirstOrDefault();
            var docId = documentId.Replace("\\", "/");
            // var docId = filePath.Replace("\\", "/");
            // Process all types (classes, interfaces, structs, enums)
            foreach (var typeDecl in root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>())
            {
                var typeChunk = new IngestedCodeChunk
                {
                    DocumentId = docId,
                    Text = typeDecl.ToFullString(),
                    Type = typeDecl switch
                    {
                        ClassDeclarationSyntax => "Class",
                        InterfaceDeclarationSyntax => "Interface",
                        StructDeclarationSyntax => "Struct",
                        RecordDeclarationSyntax => "Record",
                        _ => "Type"
                    },
                    Name = typeDecl.Identifier.Text,
                    Namespace = (typeDecl.Parent as NamespaceDeclarationSyntax)?.Name.ToString(),
                    StartLine = typeDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    EndLine = typeDecl.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                    FilePath = filePath,
                    XmlDocs = GetXmlDocs(typeDecl)
                };
                chunks.Add(typeChunk);

                // Process methods (including constructors)
                foreach (var method in typeDecl.DescendantNodes()
                    .OfType<BaseMethodDeclarationSyntax>())
                {
                    var methodName = method switch
                    {
                        ConstructorDeclarationSyntax ctor =>
                            $"{typeDecl.Identifier.Text}.{ctor.Identifier.Text}",
                        MethodDeclarationSyntax f =>
                            $"{typeDecl.Identifier.Text}.{f.Identifier.Text}",
                        _ => "UnknownMethod"
                    };

                    chunks.Add(new IngestedCodeChunk
                    {
                        DocumentId = docId,
                        Text = method.ToFullString(),
                        Type = method is ConstructorDeclarationSyntax ? "Constructor" : "Method",
                        Name = methodName,
                        ParentClass = typeDecl.Identifier.Text,
                        Parameters = string.Join(", ", method.ParameterList?.Parameters
                            .Select(p => $"{p.Type} {p.Identifier}")
                            .ToArray() ?? Array.Empty<string>()),
                        ReturnType = method is MethodDeclarationSyntax m ? m.ReturnType.ToString() : "void",
                        Attributes = string.Join(", ", method.AttributeLists
                            .SelectMany(a => a.Attributes)
                            .Select(a => a.ToString())
                            .ToArray()),
                        XmlDocs = GetXmlDocs(method),
                        StartLine = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        EndLine = method.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                        FilePath = filePath
                    });
                }

                // Process properties
                foreach (var prop in typeDecl.DescendantNodes()
                    .OfType<PropertyDeclarationSyntax>())
                {
                    chunks.Add(new IngestedCodeChunk
                    {
                        DocumentId = docId,
                        Text = prop.ToFullString(),
                        Type = "Property",
                        Name = $"{typeDecl.Identifier.Text}.{prop.Identifier.Text}",
                        ParentClass = typeDecl.Identifier.Text,
                        ReturnType = prop.Type.ToString(),
                        Attributes = string.Join(", ", prop.AttributeLists
                            .SelectMany(a => a.Attributes)
                            .Select(a => a.ToString())
                            .ToArray()),
                        XmlDocs = GetXmlDocs(prop),
                        StartLine = prop.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        EndLine = prop.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                        FilePath = filePath
                    });
                }

                // Process fields
                foreach (var field in typeDecl.DescendantNodes()
                    .OfType<FieldDeclarationSyntax>())
                {
                    foreach (var variable in field.Declaration.Variables)
                    {
                        chunks.Add(new IngestedCodeChunk
                        {
                            DocumentId = docId,
                            Text = field.ToFullString(),
                            Type = "Field",
                            Name = $"{typeDecl.Identifier.Text}.{variable.Identifier.Text}",
                            ParentClass = typeDecl.Identifier.Text,
                            ReturnType = field.Declaration.Type.ToString(),
                            Attributes = string.Join(", ", field.AttributeLists
                                .SelectMany(a => a.Attributes)
                                .Select(a => a.ToString())
                                .ToArray()),
                            XmlDocs = GetXmlDocs(field),
                            StartLine = field.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                            EndLine = field.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                            FilePath = filePath
                        });
                    }
                }

                // Process enums
                foreach (var enumDecl in typeDecl.DescendantNodes()
                    .OfType<EnumDeclarationSyntax>())
                {
                    chunks.Add(new IngestedCodeChunk
                    {
                        DocumentId = docId,
                        Text = enumDecl.ToFullString(),
                        Type = "Enum",
                        Name = $"{typeDecl.Identifier.Text}.{enumDecl.Identifier.Text}",
                        ParentClass = typeDecl.Identifier.Text,
                        StartLine = enumDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        EndLine = enumDecl.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                        FilePath = filePath,
                        XmlDocs = GetXmlDocs(enumDecl)
                    });

                    // Process enum members
                    foreach (var member in enumDecl.Members)
                    {
                        chunks.Add(new IngestedCodeChunk
                        {
                            DocumentId = docId,
                            Text = member.ToFullString(),
                            Type = "EnumMember",
                            Name = $"{enumDecl.Identifier.Text}.{member.Identifier.Text}",
                            ParentClass = enumDecl.Identifier.Text,
                            StartLine = member.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                            EndLine = member.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                            FilePath = filePath,
                            XmlDocs = GetXmlDocs(member)
                        });
                    }
                }
            }

            // Process top-level methods (for C# 9+ top-level statements)
            foreach (var method in root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Parent is not TypeDeclarationSyntax))
            {
                chunks.Add(new IngestedCodeChunk
                {
                    DocumentId = docId,
                    Text = method.ToFullString(),
                    Type = "Method",
                    Name = method.Identifier.Text,
                    Parameters = string.Join(", ", method.ParameterList.Parameters
                        .Select(p => $"{p.Type} {p.Identifier}")
                        .ToArray()),
                    ReturnType = method.ReturnType.ToString(),
                    Attributes = string.Join(", ", method.AttributeLists
                        .SelectMany(a => a.Attributes)
                        .Select(a => a.ToString())
                        .ToArray()),
                    XmlDocs = GetXmlDocs(method),
                    StartLine = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    EndLine = method.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                    FilePath = filePath
                });
            }
        }
        catch(Exception ex)
        {
            Console.WriteLine($"Error in creating code chunks{ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }



        return chunks;
    }

}
