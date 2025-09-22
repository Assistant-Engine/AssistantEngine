using AssistantEngine.UI.Services.Implementation.Database;
using OllamaSharp;
using OllamaSharp.Models;

namespace AssistantEngine.UI.Services
{
    
    public interface IDatabase
    {
        public DatabaseConfiguration Configuration { get; set;}
        public Dictionary<string, TableSchema> SQLSchema { get; set; }
        public string ExecuteSQL(string command);
        public Task<string> ExecuteSQLAsync(string command);
        public bool SetConnectionString(string connectionString);
        public Dictionary<string, TableSchema> GetSqlSchema();
        public Dictionary<string, StoredProcedure> GetStoredProcedures();
        public bool Initialise();

        //public string GetImportantTables();//algorithmicly

    }


    

}
