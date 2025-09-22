using AssistantEngine.UI.Services;
using OllamaSharp;
using OllamaSharp.Models;

namespace AssistantEngine.UI.Services.Implementation.Ollama
{
    public class OllamaModelServer : IModelServer
    {

        //public Model DefaultModel;
        public Model currentlyRunningModel { get; private set; }
        public IReadOnlyList<Model> Models { get; private set; }

        
        public async Task LoadAsync(OllamaApiClient client)
        {
            var list = await client.ListLocalModelsAsync();
            Models = list.ToList();
            
        }
    }
}
