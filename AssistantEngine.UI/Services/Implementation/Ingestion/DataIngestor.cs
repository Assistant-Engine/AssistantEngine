using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AssistantEngine.UI.Services.Implementation.Ingestion.Chunks;
using AssistantEngine.UI.Services.Models.Ingestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

namespace AssistantEngine.UI.Services.Implementation.Ingestion
{
    public class DataIngestor
    {
        private readonly ILogger<DataIngestor> _logger;
        private readonly IChunkStoreFactory _chunkFactory;
        private readonly Dictionary<string, VectorStoreCollection<string, IngestedDocument>> _docStores;

        public DataIngestor(
            ILogger<DataIngestor> logger,
            IChunkStoreFactory chunkFactory,
            IEnumerable<VectorStoreCollection<string, IngestedDocument>> documentCollections)
        {
            _logger = logger;
            _chunkFactory = chunkFactory;
            _docStores = documentCollections
                .ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);
        }
        public async Task<int> CountChunksAsync(string chunksStoreName)
        {
            var store = GetChunkStore(chunksStoreName);
            return await store.GetAsync(_ => true, int.MaxValue).CountAsync();
        }

        public async Task<int> CountDocumentsAsync(string documentsStoreName, string? sourceId = null)
        {
            var docs = GetDocStore(documentsStoreName);
            var query = sourceId is null
                ? docs.GetAsync(_ => true, int.MaxValue)
                : docs.GetAsync(d => d.SourceId == sourceId, int.MaxValue);
            return await query.CountAsync();
        }

        private IChunkStore GetChunkStore(string name) =>
            _chunkFactory.Get(name);

        private VectorStoreCollection<string, IngestedDocument> GetDocStore(string name) =>
            _docStores.TryGetValue(name, out var store)
                ? store
                : throw new KeyNotFoundException($"No document store named '{name}'");

        public static async Task IngestDataAsync(
            IServiceProvider services,
            IIngestionSource source,
            string chunksStoreName = "text-chunks",
            string documentsStoreName = "data-echoed-documents")
        {
            using var scope = services.CreateScope();
            var ingestor = scope.ServiceProvider.GetRequiredService<DataIngestor>();
            await ingestor.IngestDataAsync(source, chunksStoreName, documentsStoreName);
        }

        public async Task IngestDataAsync(
            IIngestionSource source,
            string chunksStoreName,
            string documentsStoreName)
        {
            try
            {
                var chunkStore = GetChunkStore(chunksStoreName);
                var documentStore = GetDocStore(documentsStoreName);
                var sourceId = source.SourceId;

                // 1) load existing docs
                var existingDocs = await documentStore
                    .GetAsync(d => d.SourceId == sourceId, int.MaxValue)
                    .ToListAsync();

                // 2) remove deleted
                var deleted = await source.GetDeletedDocumentsAsync(existingDocs);
                foreach (var doc in deleted)
                {
                    _logger.LogInformation("Removing {DocId}", doc.DocumentId);
                    await DeleteChunksForDocumentAsync(chunkStore, doc);
                    await documentStore.DeleteAsync(doc.Key);
                }

                // 3) upsert new/changed
                var modified = await source.GetNewOrModifiedDocumentsAsync(existingDocs);
                foreach (var doc in modified)
                {
                    try
                    {
                        _logger.LogInformation("Processing {DocId}", doc.DocumentId);
                        await DeleteChunksForDocumentAsync(chunkStore, doc);
                        await documentStore.UpsertAsync(doc);

                        var chunks = await source.CreateChunksForDocumentAsync(doc);
                        await chunkStore.UpsertAsync(chunks);
                    }
                    catch(Exception ex)
                    {
                        _logger.LogError(ex, $"Error ingesting document: {doc.DocumentId}");
                    }
              
                }
                _logger.LogInformation("Ingestion is up-to-date");

            }
            catch (Exception ex)
            {
                _logger.LogError(ex,$"Error ingesting data");
            }
     
      
        }
        public async Task DeleteDocumentAsync(string documentsStoreName, string chunksStoreName, string documentId)
        {
            var chunkStore = GetChunkStore(chunksStoreName);
            var documentStore = GetDocStore(documentsStoreName);

            var doc = await documentStore.GetAsync(d => d.DocumentId == documentId, 1).FirstOrDefaultAsync();
            if (doc == null)
            {
                _logger.LogWarning("Document {DocumentId} not found", documentId);
                return;
            }

            await DeleteChunksForDocumentAsync(chunkStore, doc);
            await documentStore.DeleteAsync(doc.Key);

            _logger.LogInformation("Deleted document {DocumentId}", documentId);
        }
        public async Task DeleteSourceAsync(string documentsStoreName, string chunksStoreName, string sourceId)
        {
            var chunkStore = GetChunkStore(chunksStoreName);
            var documentStore = GetDocStore(documentsStoreName);

            var docs = await documentStore.GetAsync(d => d.SourceId == sourceId, int.MaxValue).ToListAsync();
            foreach (var doc in docs)
            {
                await DeleteChunksForDocumentAsync(chunkStore, doc);
                await documentStore.DeleteAsync(doc.Key);
            }

            _logger.LogInformation("Deleted {Count} documents for source {SourceId}", docs.Count, sourceId);
        }
        public static async Task DeleteChunksForDocumentAsync(
            IChunkStore store,
            IngestedDocument doc)
        {
            var toDelete = await store
                .GetAsync(c => c.DocumentId == doc.DocumentId, int.MaxValue)
                .ToListAsync();

            if (toDelete.Any())
                await store.DeleteAsync(toDelete.Select(c => c.Key));
        }
    }
}
