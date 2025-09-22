using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using AssistantEngine.UI.Services;

namespace AssistantEngine.UI.Services
{
    public sealed class DisabledEmbeddingGenerator 
    {
        private readonly string _reason;
        public DisabledEmbeddingGenerator(string reason) => _reason = reason;

        public Task<Embedding> GenerateEmbeddingAsync(string input, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromException<Embedding>(new InvalidOperationException(_reason));

        public Task<IList<Embedding>> GenerateEmbeddingsAsync(IList<string> inputs, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromException<IList<Embedding>>(new InvalidOperationException(_reason));
    }
}
