using AssistantEngine.UI.Pages.Chat;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace AssistantEngine.UI.Services.Implementation.Factories
{
        public delegate AIAgent AIAgentFactory(string? modelId = null);
    public delegate IOllamaApiClient OllamaClientFactory(string modelId);
        public delegate IChatClient ChatClientFactory(string modelId);
        public delegate IEmbeddingGenerator<string, Embedding> EmbedClientFactory(string modelId);
}
