using AssistantEngine.UI.Pages.Chat;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel.Text;
using Newtonsoft.Json;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using AssistantEngine.UI.Services.Implementation.Database;
using AssistantEngine.UI.Services.Implementation.Factories;
using AssistantEngine.UI.Services.Models.Ingestion;
using AssistantEngine.UI.Services.Models;

namespace AssistantEngine.UI.Services.Implementation.Ingestion;

//    Task<IEnumerable<IIngestedChunk>> CreateChunksForDocumentAsync(IngestedDocument document);  //any additional descriptive process goes here
// IDatabase can be registered with a desciption model
public class DatabaseIngestionSource(IDatabase database, ChatClientFactory chatClientFactory, AssistantConfig config) : IIngestionSource
{

    //so here document id refers to the database name + table name so document id refers to a table.
    //but how many chunks should a table actually contain?
    //a one to one mapping might be fine.


    //if document id was the whole database
    //it would reingest the whole database every time


    public bool DescribeTablesWithAI { get; set; } = database.Configuration.DescribeDatabaseWithModel; //if true, will use AI to describe the tables

    public IChatClient descriptorClient { get; private set; } = chatClientFactory(config.DescriptorModel.ModelId);

    public event Action<string>? StatusMessage;
    private void OnStatus(string msg) => StatusMessage?.Invoke(msg);
    public string SourceFileId(string tableName)
       => database.Configuration.Id + "."+tableName;
    public static string SourceTableVersion(TableSchema schema) => schema.Fields.Count().ToString();

    public string SourceId => $"{database.Configuration.Id}";

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
        var allTables = database.GetSqlSchema();

        foreach (var table in allTables)
        {
            var id = SourceFileId(table.Key);
            OnStatus($"Ingesting {id}");
            var version = SourceTableVersion(table.Value);

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

        var currentTables = database.GetSqlSchema();

        var currentFileIds = currentTables.Select(x=>x.Value.TableName).ToLookup(SourceFileId);

        var deletedDocuments = existingDocuments.Where(d => !currentFileIds.Contains(d.DocumentId));
        foreach (var deleted in deletedDocuments)
        {
            OnStatus($"Deleted {deleted.DocumentId}");
        }
        return Task.FromResult(deletedDocuments);
    }

    public async Task<IEnumerable<IIngestedChunk>> CreateChunksForDocumentAsync(IngestedDocument document)
    {
        try
        {
            var currentTables = database.GetSqlSchema();
            //var filePath = Path.Combine(sourceDirectory, document.DocumentId);
            OnStatus($"Reading Table {document.DocumentId}");

            var correctTable = currentTables.Where(x => $"{SourceId}.{x.Key}" == document.DocumentId);

            if (!correctTable.Any())
            {
                Console.WriteLine($"Could not find document to make chunks for. Looking for DocumentID table {document.DocumentId}");
            }
            foreach (var table in correctTable)
            {
                return await GetChunks(table.Value);
            }
        }
        catch(Exception ex)
        {
         
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.StackTrace);
        }
        return Enumerable.Empty<IIngestedChunk>();//mabe remove later
        return null;

    }

    public async Task<List<IngestedSQLTableChunk>> GetChunks(TableSchema tableSchema)
    {
        var chunksToReturn = new List<IngestedSQLTableChunk>();

        // Base metadata
        var chunk = new IngestedSQLTableChunk
        {
            Key = Guid.NewGuid().ToString(),
            TableName = tableSchema.TableName,
            DatabaseId = database.Configuration.Id,
            DatabaseName = database.Configuration.Id,
            DocumentId = $"{database.Configuration.Id}.{tableSchema.TableName}",
            Fields = string.Join(",", tableSchema.Fields.Select(f => f.FieldName)),
            FieldDataTypes = string.Join(",", tableSchema.Fields.Select(f => f.DataType))
        };

        // Example queries
        var fieldList = string.Join(", ", tableSchema.Fields.Select(f => f.FieldName));
        var example1 = $"SELECT {fieldList} FROM {tableSchema.TableName};";
        var example2 = $"SELECT * FROM {tableSchema.TableName} WHERE {tableSchema.Fields.First().FieldName} = '{tableSchema.Fields.First().ExampleValueStringConverted}';";
        chunk.ExampleQuery = example1; // primary example

        // Optionally describe with AI
        string description = $"(no description available)";
        if (DescribeTablesWithAI && descriptorClient != null)
        {
            var systemPrompt = @"
You’re a schema-insight assistant for AIO Exchange.
Input: a table name and its columns (including example values).
Output: exactly three sections, each prefixed by its header:

Summary:
– A one-line description of the table’s purpose (≤100 chars)

Intents:
– Two to three bullet points describing human-readable query goals

Examples:
– Two SQL example SELECTs:
  1. A `SELECT Col1, Col2, …, ColN FROM <TableName>` listing every column explicitly.
  2. A `SELECT * FROM <TableName>` with a `WHERE` clause that filters on the provided example values.

Return only these sections—no extra text.";

            var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, systemPrompt),
            new ChatMessage(ChatRole.User,   JsonConvert.SerializeObject(tableSchema))
        };
            var response = await descriptorClient.GetResponseAsync(messages);
            description = response.Messages
                                  .Where(m => m.Role == ChatRole.Assistant)
                                  .LastOrDefault()?.Text
                                  .Trim() ?? description;
        }

        // Build the nicely formatted Text field
        var sb = new StringBuilder();
        sb.AppendLine($"Table: {tableSchema.TableName}");
        sb.AppendLine();
        sb.AppendLine("Description:");
        sb.AppendLine(description);
        sb.AppendLine();
        sb.AppendLine("Fields:");
        foreach (var field in tableSchema.Fields)
        {
            var exampleText = string.IsNullOrEmpty(field.ExampleValueStringConverted)
                ? "No example"
                : $"Example: '{field.ExampleValueStringConverted}'";
            sb.AppendLine($"- {field.FieldName} ({field.DataType}): {exampleText}");
        }
        sb.AppendLine();
        sb.AppendLine("Examples:");
        sb.AppendLine($"1. {example1}");
        sb.AppendLine($"2. {example2}");

        chunk.Text = sb.ToString();
        chunksToReturn.Add(chunk);

        return chunksToReturn;
    }


}
