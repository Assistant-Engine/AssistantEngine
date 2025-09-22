// AssistantEngine.Core/Config/AppConfig.cs
using System.Text.Json.Serialization;

namespace AssistantEngine.UI.Services.Implementation.Config
{

    public sealed class AppConfig
    {
        //public string OllamaUrl { get; set; } = "http://localhost:11434";

        /// <summary>
        /// Full path to the vector store file (e.g., C:\...\App_Data\vector-store-main-7.db).
        /// </summary>
        public string VectorStoreFilePath { get; set; } = string.Empty;

        /// <summary>
        /// Folder where model JSONs live (descriptors, roles, etc.).
        /// Defaults to <base>/Config/Models.
        /// </summary>
        public string ModelFilePath { get; set; } = string.Empty;

        // Convenience (not serialized)
        [JsonIgnore]
        public string VectorStoreDirectory =>
            string.IsNullOrWhiteSpace(VectorStoreFilePath)
                ? string.Empty
                : Path.GetDirectoryName(VectorStoreFilePath) ?? string.Empty;

        [JsonIgnore]
        public string VectorStoreFileName =>
            string.IsNullOrWhiteSpace(VectorStoreFilePath)
                ? string.Empty
                : Path.GetFileName(VectorStoreFilePath);
    }

}
