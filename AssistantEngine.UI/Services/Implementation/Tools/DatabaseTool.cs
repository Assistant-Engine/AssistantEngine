using AssistantEngine.Services.Extensions;
using AssistantEngine.UI.Services.Implementation.Database;
using AssistantEngine.UI.Services.Implementation.Factories;
using AssistantEngine.UI.Services.Models;
using Microsoft.Extensions.AI;
using Microsoft.SqlServer.Dac.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Microsoft.Win32;
using Newtonsoft.Json;
using OllamaSharp.Models;
using System.ComponentModel;
using System.Security;
using System.Text.RegularExpressions;
using static AssistantEngine.Services.Extensions.ChatMessageExtensions;

namespace AssistantEngine.Services.Implementation.Tools
{



    public class DatabaseTool : ITool
    {
        private readonly IToolStatusNotifier _notifier;
        private readonly ChatClientFactory _chatClientFactory;
        private readonly IDatabaseRegistry _dbRegistry;
        private readonly SemanticSearch _search;
        private readonly Func<AssistantConfig> _getConfig;

        public DatabaseTool(
            SemanticSearch search,
               IDatabaseRegistry dbRegistry,                 // 👈 inject registry
            ChatClientFactory chatClientFactory,
            Func<AssistantConfig> getConfig,
            IToolStatusNotifier notifier)
        {
            _dbRegistry = dbRegistry;
            _search = search;
            _chatClientFactory = chatClientFactory;
            _getConfig = getConfig;
            _notifier = notifier;
        }

        private AssistantConfig CurrentConfig => _getConfig();

        private ChatOptions T2SqlChatOptions =>
            CurrentConfig.ModelOptions.FirstOrDefault(m => m.Key == "Text2SQL")?.Options
            ?? CurrentConfig.ModelOptions.First(m => m.Key == "Assistant").Options;

        private ChatOptions CorrectionChatOptions =>
            CurrentConfig.ModelOptions.FirstOrDefault(m => m.Key == "Correction")?.Options
            ?? CurrentConfig.ModelOptions.First(m => m.Key == "Assistant").Options;


      
        [Description("Searches the SQL schema to find relevant information on tables to use for queries")]
        public async Task<IEnumerable<string>> SearchDatabaseSchema(
        [Description("The database id")] string database,
        [Description("Search Phrase e.g. Live Market Data")] string searchPhrase)
        {

            Console.WriteLine($"Searching Database {database} for search phrase {searchPhrase}");

            var filters = new Dictionary<string, string>
            {
                ["DatabaseId"] = database
            };
            ///   await InvokeAsync(StateHasChanged);
            var results = await _search.SearchAsync("sql-table-chunks", searchPhrase, filters, maxResults: 4);


            Console.WriteLine("Searching schema gave the following results");
            foreach(var result in results)
            {
                Console.WriteLine("Result: \n ");
                Console.WriteLine(JsonConvert.SerializeObject(result.Text));
                Console.WriteLine("Result End \n ");
                
            }

            var databaseConsiderations = _dbRegistry.Get(database)?.Configuration.DatabaseConsiderations;
            List<string> resultsSet = new List<string>();
            if (!string.IsNullOrEmpty(databaseConsiderations))
            {
                resultsSet.Add($"Consider the additional requirements when executing sql: {databaseConsiderations}"); //how do we know this is correct?
            } 
             resultsSet.AddRange(results.Select(result =>
                $"<result filename=\"{result.DocumentId}\">{result.Text}</result>"));
            return resultsSet;
            
        }
       
        [Description("Searches the SQL schema to find relevant information for a table and a searchphrase")]
        public async Task<IEnumerable<string>> SearchSQLSchema(
  [Description("The database id e.g. AIOExchange")] string database,
  [Description("Search Phrase e.g. Live Market Data")] string searchPhrase
)
        {
            Console.WriteLine($"Searching Database {database} for search phrase {searchPhrase}");



            var filters = new Dictionary<string, string>
            {
                ["DatabaseId"] = database
            };
            ///   await InvokeAsync(StateHasChanged);
            var results = await _search.SearchAsync("sql-table-chunks", searchPhrase, filters, maxResults: 5);

            var databaseConsiderations = _dbRegistry.Get(database)?.Configuration.DatabaseConsiderations;
            Console.WriteLine("Searching schema gave the following results");
            foreach (var result in results)
            {
                Console.WriteLine("Result: \n ");
                Console.WriteLine(JsonConvert.SerializeObject(result));
                Console.WriteLine("Result End \n ");

            }
            List<string> resultsSet = new List<string>();
            if (!string.IsNullOrEmpty(databaseConsiderations))
            {
                resultsSet.Add($"Consider the additional requirements when executing sql: {databaseConsiderations}"); //how do we know this is correct?
            }
            resultsSet.AddRange(results.Select(result =>
               $"<result filename=\"{result.DocumentId}\">{result.Text}</result>"));
            return resultsSet;
        }

        

        [Description("Gets the SQL schema of all the tables in a database. Prioritize use of SearchSQLSchema instead.")]
        public async Task<Dictionary<string, TableSchema>> GetAllSQLSchema([Description("The database name")] string database)
        {
            try
            {
                var db = _dbRegistry.Get(database);    // 👈 lookup from registry
                if (db == null) throw new Exception($"Unknown database {database}");
                return db.GetSqlSchema();
            }
            catch(Exception ex)
            {
                Dictionary<string, TableSchema> returnVal = new Dictionary<string, TableSchema>();
                returnVal.Add(ex.Message, null); //for the llm to easily handle
                return returnVal;
            }
      
        }

        [Description("Executes an SQL query")]
        public async Task<string> ExecuteSQL(
                [Description("The database id")] string database,
                [Description("The SQL query to execute")] string sqlQuery)
        {
            Console.WriteLine($"Executing SQL {sqlQuery} on database {database}");

            try
            {
                _notifier.StatusMessage("Validating query");

                var parser = new TSql150Parser(false);
                using var rdr = new StringReader(sqlQuery);
                var frag = parser.Parse(rdr, out var errors);

                var visitor = new TableColumnVisitor();
                frag.Accept(visitor);

                var db = _dbRegistry.Get(database);   // 👈 live lookup
                if (db == null)
                    return $"<error message=\"Unknown database '{database}'\" />";

                var schema = db.GetSqlSchema();
                foreach (var tbl in visitor.TableNames)
                {
                    if (!schema.ContainsKey(tbl))
                        return $"<error message=\"Table '{tbl}' not in schema\" />";
                }

                _notifier.StatusMessage("Query Valid");
                return await db.ExecuteSQLAsync(sqlQuery);
            }
            catch (Exception ex)
            {
                var msg = $"The following exception was thrown {ex.Message} in ExecuteSQL Tool. " +
                          $"Ensure the schema is correct using the SearchDatabaseSchema tool before executing this function";
                Console.WriteLine(msg);
                return $"<error message=\"{msg}\" />";
            }
        }

        [Description("Gets known databases")]
        public async Task<string[]> GetKnownDatabases(string anyparam = null)
        {
            return _dbRegistry.All.Keys.ToArray();   // 👈 live keys
        }


    }
}
