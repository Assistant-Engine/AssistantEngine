using AssistantEngine.UI.Pages.Chat;

using Microsoft.Extensions.AI;
using OllamaSharp;

namespace AssistantEngine.UI.Services.Implementation.Factories
{      
        public delegate IOllamaApiClient OllamaClientFactory(string modelId);
        public delegate IChatClient ChatClientFactory(string modelId);
        public delegate IEmbeddingGenerator<string, Embedding> EmbedClientFactory(string modelId);
}
