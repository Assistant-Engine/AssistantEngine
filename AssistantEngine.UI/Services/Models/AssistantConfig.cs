using AssistantEngine.UI.Services.Implementation.Database;
using Microsoft.Extensions.AI;

namespace AssistantEngine.UI.Services.Models
{
    public class NamedModelOption
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public ChatOptions Options { get; set; }
    }
    public class AssistantConfig
    {
        public bool Default { get; set; } = false;
        public string Name { get; set; } = "DefaultModelConfig";
        public float Version { get; set; } = 1;
        public string Id { get; set; } = "DefaultModelConfigId";

        /// <summary>
        /// for tools that require their own model settngs, you should use the dictionary to define the model options.
        /// </summary>
        public List<NamedModelOption> ModelOptions { get; set; } = new()
        {
        };


        public ChatOptions AssistantModel => ModelOptions.First(m => m.Key == "Assistant").Options;
        public ChatOptions EmbeddingModel => ModelOptions.First(m => m.Key == "Embedding").Options;
        public ChatOptions DescriptorModel => ModelOptions.First(m => m.Key == "Descriptor").Options;
        public ChatOptions CorrectionModel => ModelOptions.First(m => m.Key == "Correction").Options;
        public ChatOptions Text2SQLModel => ModelOptions.First(m => m.Key == "Text2SQL").Options;
        public ChatOptions MiniTaskModel => ModelOptions.First(m => m.Key == "MiniTask").Options;

        public List<McpConnectorConfig> McpConnectors { get; set; } = new();

        public List<DatabaseConfiguration> Databases { get; set; } = new();
        public string ModelProvider { get; set; } = "Ollama"; //default and only currently;
        public string ModelProviderUrl { get; set; } = "http://178.196.27.11:11434"; //TOREMOVE for testing mac
        public string SystemPrompt { get; set; } = "You are a helpful assistant. Answer the user's questions based on the provided context.";


        public string? VectorStore { get; set; } = "local";//not used

        public List<IngestionSourceFolder> IngestionPaths { get; set; } = new List<IngestionSourceFolder>();



        public List<string> EnabledFunctions { get; set; } = new List<string>() { "SearchAsync", "GetWeather", "GetKnownDatabases", "GetAllSQLSchema", "ExecuteSQL", "SearchDatabaseSchema" };

        public string Description { get; set; } = "Welcome to Assistant Engine...";

        public bool EnableThinking { get; set; } = true;
        public bool PersistThoughtHistory { get; set; } = true;


    }
    public class IngestionSourceFolder
    {
        public string Path { get; set; } = "C:\\DefaultPath";

        public bool ExploreSubFolders { get; set; } = true;

        public List<string> FileExtensions { get; set; } = null; //all

    };
}
  

