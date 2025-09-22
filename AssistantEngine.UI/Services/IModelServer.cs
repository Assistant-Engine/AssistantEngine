using OllamaSharp;
using OllamaSharp.Models;

namespace AssistantEngine.UI.Services
{
    
    public interface IModelServer
    {
        
        IReadOnlyList<Model> Models { get; }
        Task LoadAsync(OllamaApiClient client);

        
    }
}
