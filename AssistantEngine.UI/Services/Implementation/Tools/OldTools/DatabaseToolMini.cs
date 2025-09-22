using Microsoft.Extensions.AI;
using Microsoft.SqlServer.Dac.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Newtonsoft.Json;
using static AssistantEngine.Services.Extensions.ChatMessageExtensions;
using OllamaSharp.Models;
using System.ComponentModel;
using System.Security;
using System.Text.RegularExpressions;
using AssistantEngine.Services.Extensions;
using AssistantEngine.Services.Implementation;
using AssistantEngine.Services.Implementation.Tools;
using AssistantEngine.UI.Services.Implementation.Database;
using AssistantEngine.UI.Services.Implementation.Factories;
using AssistantEngine.UI.Services.Models;

namespace AssistantEngine.UI.Services.Implementation.Tools.OldTools
{


    /// <summary>
    /// this provides more simple tasks 
    /// </summary>
    public class DatabaseToolMini
    {

        private readonly IToolStatusNotifier _notifier;
        ChatClientFactory _chatClientFactory;
        Dictionary<string, IDatabase> databaseDict;
        AssistantConfig _currentConfig;
        IChatClient _t2sqlClient;
        ChatOptions _t2sqlChatOptions;
        ChatOptions _correctionChatOptions;
        //so next what does this neeed to do? it needs to embed a description.
        //we can inject the model.
        private readonly SemanticSearch _search;

        public DatabaseToolMini(SemanticSearch search, IEnumerable<IDatabase> Databases, ChatClientFactory chatClientFactory, AssistantConfig config, IToolStatusNotifier notifier)
        {
            databaseDict = Databases.ToDictionary(db => db.Configuration.Id, db => db);
            _search = search;
            _chatClientFactory = chatClientFactory;
            _currentConfig = config; _notifier = notifier;
            var assistantOptions = config.ModelOptions
            .First(m => m.Key == "Assistant")
            .Options;

            // try Text2SQL / Correction, else use Assistant
            _t2sqlChatOptions = config.ModelOptions
                .FirstOrDefault(m => m.Key == "Text2SQL")?.Options
                ?? assistantOptions;

            _correctionChatOptions = config.ModelOptions
                .FirstOrDefault(m => m.Key == "Correction")?.Options
                ?? assistantOptions;
        }

        [Description("Searches the database given a user intent")]
        public async Task<IEnumerable<string>> SearchDatabase(
     [Description("The database name")] string database,
     [Description("User Intent (e.g. Fetch the current price of Bitcoin today)")] string userIntent//,
     //filters
     //[Description("If set, only search in that table.")] string? tableNameFilter = null
     )
        {
            try
            {
                // grab Assistant modelId for fallback
                var assistantModelId = _currentConfig.ModelOptions
                    .First(m => m.Key == "Assistant").Options.ModelId;

                string correctionModelId = _currentConfig.ModelOptions
                    .FirstOrDefault(m => m.Key == "Correction")?.Options.ModelId
                    ?? assistantModelId;

                string text2SqlModelId = _currentConfig.ModelOptions
                    .FirstOrDefault(m => m.Key == "Text2SQL")?.Options.ModelId
                    ?? assistantModelId;

                Console.WriteLine($"Searching schema in “{database}” for “{userIntent}”");
                _notifier.StatusMessage($"Searching schema in “{database}” for “{userIntent}”");
                // 1) derive & refine semantic‐search phrase with up to 3 AI‐driven retries
                const int MaxSchemaRetries = 3;
                int attempt = 0;
                string semanticSearchPhrase = userIntent;
                IEnumerable<string> tableChunks;

                do
                {
                    attempt++;
                    tableChunks = await SearchDatabaseSchema(database, semanticSearchPhrase);

                    if (tableChunks.Any())
                        break;

                    if (attempt >= MaxSchemaRetries)
                        return new[]
                        {
                    $"<error message=\"No relevant tables found for intent '{userIntent}' after {MaxSchemaRetries} attempts\" />"
                };

                    // ask AI for a better search phrase
                    var phraseClient = _chatClientFactory(correctionModelId);
                    var resp = await phraseClient.GetResponseAsync(new[]
                    {
                new ChatMessage(ChatRole.System,
                    "You are a helpful assistant that rewrites user intents into concise semantic search phrases."),
                new ChatMessage(ChatRole.User,
                    $@"The user intent “{userIntent}” yielded no schema hits when searching for “{semanticSearchPhrase}”.
Suggest a new, shorter search phrase (just the phrase itself).")
            }, _correctionChatOptions);
                    semanticSearchPhrase = resp.Messages
                        .Last(m => m.Role == ChatRole.Assistant)
                        .Text
                        .Trim();

                    Console.WriteLine($"Retrying schema search with alternative phrase: {semanticSearchPhrase}");
                }
                while (true);
                _notifier.StatusMessage("Schema search complete.");
                // 2) build SQL via AI
                var queryClient = _chatClientFactory(text2SqlModelId);
                var systemMsg = new ChatMessage(
                    ChatRole.System,
                    "You are an expert SQL assistant. Given a list of schema snippets and a user intent, output only a valid SQL query."
                );
                var userMsg = new ChatMessage(
                    ChatRole.User,
                    $@"
Database: {database}
Schemas:
{string.Join("\n", tableChunks)}

User intent: {userIntent}

Please write an optimized SQL query that satisfies the intent, referencing only the above tables."
                );

                var aiResp = await queryClient.GetResponseAsync(new[] { systemMsg, userMsg }, _t2sqlChatOptions);
                var sqlQuery = aiResp.Messages
                                     .Where(m => m.Role == ChatRole.Assistant)
                                     .Last()
                                     .Text
                                     .Trim();

             
                // 3) execute & auto‐improve the SQL
                var result = await ExecuteSQL(database, sqlQuery, userIntent);
                return new[] { result };
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                return Enumerable.Empty<string>();
            }
        }





        [Description("Searches the SQL schema to find relevant information on tables to use for queries")]
        public async Task<IEnumerable<string>> SearchDatabaseSchema(
        [Description("The database name")] string database,
        [Description("Search Phrase e.g. Live Market Data")] string searchPhrase)
        {

            Console.WriteLine($"Searching Database {database} for search phrase {searchPhrase}");


            var filters = new Dictionary<string, string>();
      
            ///   await InvokeAsync(StateHasChanged);
            var results = await _search.SearchAsync("sql-table-chunks", searchPhrase, filters, maxResults: 4);


            Console.WriteLine("Searching schema gave the following results");
            foreach(var result in results)
            {
                Console.WriteLine("Result: \n ");
                Console.WriteLine(JsonConvert.SerializeObject(result.Text));
                Console.WriteLine("Result End \n ");
                
            }


            return results.Select(result =>
                $"<result filename=\"{result.DocumentId}\" page_number=\"1\">{result.Text}</result>");
        }
       
        [Description("Searches the SQL schema to find relevant information for a table and a searchphrase")]
        public async Task<IEnumerable<string>> SearchSQLSchema(
  [Description("The database name e.g. AIOExchange")] string database,
  [Description("Search Phrase e.g. Live Market Data")] string searchPhrase
)
        {
            Console.WriteLine($"Searching Database {database} for search phrase {searchPhrase}");


            var filters = new Dictionary<string, string>();

            ///   await InvokeAsync(StateHasChanged);
            var results = await _search.SearchAsync("sql-table-chunks", searchPhrase, filters, maxResults: 5);


            Console.WriteLine("Searching schema gave the following results");
            foreach (var result in results)
            {
                Console.WriteLine("Result: \n ");
                Console.WriteLine(JsonConvert.SerializeObject(result));
                Console.WriteLine("Result End \n ");

            }


            return results.Select(result =>
                $"<result filename=\"{result.DocumentId}\" page_number=\"1\">{result.Text}</result>");
        }


        [Description("Gets the SQL schema of all the tables in a database")]
        public async Task<Dictionary<string, TableSchema>> GetAllSQLSchema([Description("The database name")] string database)
        {
            return databaseDict[database].GetSqlSchema();
        }

        [Description("Executes an SQL query")]
        public async Task<string> ExecuteSQL([Description("The database name")] string database, [Description("The SQL query to execute")] string sqlQuery, [Description("The user intent")] string userIntent)
        {

            Console.WriteLine($"Executing SQL {sqlQuery} on database {database}");
            try
            {
                _notifier.StatusMessage($"Validating query");

                var parser = new TSql150Parser(false);
                TSqlFragment frag;
                using (var rdr = new StringReader(sqlQuery))
                    frag = parser.Parse(rdr, out var errors);

                var visitor = new TableColumnVisitor();
                frag.Accept(visitor);

                if (!databaseDict.ContainsKey(database))
                {
                    string exms = $"<error message=\"Unknown database '{database}'\" />";
                    Console.WriteLine(exms);
                    return exms;

                }
              

                var db = databaseDict[database];
                var schema = db.GetSqlSchema();

                foreach (var tbl in visitor.TableNames)
                {
                    if (!schema.ContainsKey(tbl))
                    {
                        string exms = $"<error message=\"Table '{tbl}' not in schema\" />";
                        Console.WriteLine(exms);
                        return exms;
                    }
                       
                }
                _notifier.StatusMessage($"Query Valid");


                var result = await ImproveAndExecuteSQLQuery(db, userIntent, sqlQuery, schema);
                //var result = await db.ExecuteSQLAsync(sqlQuery);
                return result;
            }
            catch(Exception ex)
            {
                _notifier.StatusMessage($"The following exception was thrown {ex.Message} in ExecuteSQL");
                string exception = $"The following exception was thrown {ex.Message} in ExecuteSQL Tool" +
                    $"Ensure the schema is correct using the SearchDatabaseSchema tool before executing this function";
                Console.WriteLine(exception);
                return $"<error message=\"{exception}\" />";
              

                
            }
        
        }

        /// <summary>
        /// the purpose of this is to improve any sql query and execute it. 
        /// 
        /// It should check without ai if it is using correct column names, otherwise it should inform the ai to fix it.
        /// It should check WITH AI, that the query is not over filtering, and it is using the correct columns for the user intent. 
        /// </summary>
        /// <param name="query"></param>
        /// <param name="details"></param>
        /// <returns></returns>
        /// 
        public async Task<string> ImproveAndExecuteSQLQuery(
            IDatabase database,
            string userIntent,
            string query,
            Dictionary<string, TableSchema> databaseSchema)
        {
            const int MaxIterations = 7;
            int iterationCount = 0;

            _notifier.StatusMessage($"Starting ImproveAndExecuteSQLQuery");
            var assistantModelId = _currentConfig.ModelOptions
    .First(m => m.Key == "Assistant").Options.ModelId;

            string correctionModelId = _currentConfig.ModelOptions
                .FirstOrDefault(m => m.Key == "Correction")?.Options.ModelId
                ?? assistantModelId;

            string text2SqlModelId = _currentConfig.ModelOptions
                .FirstOrDefault(m => m.Key == "Text2SQL")?.Options.ModelId
                ?? assistantModelId;
            _t2sqlClient = _chatClientFactory(text2SqlModelId);
            var systemMessage = new ChatMessage(
                ChatRole.System,
                "You are an SQL assistant. Given schema info and errors, output only corrected SQL queries." //CHANGE THIS
               // + (string.IsNullOrEmpty(_currentConfig.DatabaseConsiderations) ? "" : $"Consider the additional requirements: {_currentConfig.DatabaseConsiderations}")
              //  + (string.IsNullOrEmpty(database.Dialect) ? "" : $"The SQL dialect should be: {database.Dialect}")
            );

            string sqlQuery = query;
            string lastAssistantFix = null;
            TableSchema currentTableSchema = null;

            while (true)
            {
                iterationCount++;
                if (iterationCount > MaxIterations)
                    throw new InvalidOperationException(
                        $"Unable to converge after {MaxIterations} attempts."
                    );

                _notifier.StatusMessage($"Iteration #{iterationCount}: Starting cycle");

                //
                // 1) Schema check
                //
                _notifier.StatusMessage($"Iteration #{iterationCount}: Schema check");

                var parser = new TSql150Parser(false);
                using var rdr = new StringReader(sqlQuery);
                var frag = parser.Parse(rdr, out var parseErrors);

                var schemaErrors = new List<string>();

                // 1a) capture any parser errors
                //if (parseErrors.Count > 0)
                  //  schemaErrors.AddRange(parseErrors
                    //    .Select(e => $"Parse error at Line {e.Line}, Col {e.Column}: {e.Message}"));

                // 1b) run your visitor but catch *any* exception
                var visitor = new TableColumnVisitor();
                try
                {
                    frag.Accept(visitor);
                }
                catch (Exception ex)
                {
                    schemaErrors.Add($"Schema‐visitor error: {ex.Message}");
                }

                // 1c) now your existing logic over visitor.ColumnReferences
                foreach (var colRef in visitor.ColumnReferences)
                {
                    if (!databaseSchema.TryGetValue(colRef.Table, out currentTableSchema))
                    {
                        schemaErrors.Add($"Table '{colRef.Table}' not found.");
                        continue;
                    }
                    var actualFields = currentTableSchema.Fields.Select(f => f.FieldName).ToList();
                    if (!actualFields.Contains(colRef.Column, StringComparer.OrdinalIgnoreCase))
                    {
                        schemaErrors.Add(
                            $"Field '{colRef.Column}' not found on '{colRef.Table}'. " +
                            $"Available: {string.Join(", ", actualFields)}"
                        );
                    }
                }


                if (schemaErrors.Any())
                {
                    _notifier.StatusMessage(
                        $"Iteration #{iterationCount}: Schema errors ({schemaErrors.Count}): " +
                        $"{string.Join("; ", schemaErrors)} – requesting AI fix"
                    );

                    var prompt = $@"
The SQL has these schema errors:
{string.Join("\n", schemaErrors)}
Please output only a corrected SQL query that fixes them.";
                    var convo = new List<ChatMessage> { systemMessage };
                    if (lastAssistantFix != null)
                        convo.Add(new ChatMessage(ChatRole.Assistant, lastAssistantFix));
                    convo.Add(new ChatMessage(ChatRole.User, prompt));

                    var resp = await _t2sqlClient.GetResponseAsync(convo, _t2sqlChatOptions);
                    lastAssistantFix = resp.Messages
                                         .Where(m => m.Role == ChatRole.Assistant)
                                         .Last()
                                         .Text
                                         .Trim();

                    _notifier.StatusMessage($"Iteration #{iterationCount}: Received corrected SQL from AI");
                    sqlQuery = lastAssistantFix;
                    continue;
                }
                else
                {
                    _notifier.StatusMessage($"Iteration #{iterationCount}: Schema valid");
                }

                //
                // 2) Filter & column‐validation
                //
                _notifier.StatusMessage($"Iteration #{iterationCount}: Filter & column validation");
                var fvPrompt = $@"
User intent: {userIntent}
Current SQL:
{sqlQuery}
Please confirm:
1) selects correct columns (and suggests useful extras),
2) does not over‐filter,
3) uses correct formats per example values.

If OK, respond exactly NO_CHANGE. Otherwise output only the corrected SQL.

Here is the table schema: {JsonConvert.SerializeObject(currentTableSchema)}
";
                var fvConvo = new List<ChatMessage> { systemMessage };
                if (lastAssistantFix != null)
                    fvConvo.Add(new ChatMessage(ChatRole.Assistant, lastAssistantFix));
                fvConvo.Add(new ChatMessage(ChatRole.User, fvPrompt));

                var fvResp = await _t2sqlClient.GetResponseAsync(fvConvo, _t2sqlChatOptions);
                var suggestion = fvResp.Messages
                                    .Where(m => m.Role == ChatRole.Assistant)
                                    .Last()
                                    .Text
                                    .Trim();
                var cleanSuggestion = suggestion.RemoveThinkTags();

                if (!cleanSuggestion.Equals("NO_CHANGE", StringComparison.OrdinalIgnoreCase))
                {
                    _notifier.StatusMessage(
                        $"Iteration #{iterationCount}: AI suggested SQL change – applying and looping"
                    );
                    lastAssistantFix = cleanSuggestion;
                    sqlQuery = cleanSuggestion;
                    continue;
                }

                _notifier.StatusMessage($"Iteration #{iterationCount}: No filter/column changes needed");

                //
                // 3) Execute SQL
                //
                _notifier.StatusMessage($"Iteration #{iterationCount}: Executing SQL");
                var result = await database.ExecuteSQLAsync(sqlQuery);
                _notifier.StatusMessage(
                    $"Iteration #{iterationCount}: Execution result: " +
                    $"{(result.Length > 100 ? result.Substring(0, 100) + "..." : result)}"
                );

                if (result.StartsWith("<error", StringComparison.OrdinalIgnoreCase))
                {
                    _notifier.StatusMessage(
                        $"Iteration #{iterationCount}: Execution error – requesting AI fix"
                    );
                    var execPrompt = $@"
Execution returned an error:
{result}
This Table Schema (for reference): {JsonConvert.SerializeObject(currentTableSchema)}
Please output only a corrected SQL query that fixes this error.";
                    var exConvo = new List<ChatMessage> { systemMessage };
                    if (lastAssistantFix != null)
                        exConvo.Add(new ChatMessage(ChatRole.Assistant, lastAssistantFix));
                    exConvo.Add(new ChatMessage(ChatRole.User, execPrompt));

                    var exResp = await _t2sqlClient.GetResponseAsync(exConvo, _t2sqlChatOptions);
                    lastAssistantFix = exResp.Messages.Where(m => m.Role == ChatRole.Assistant).Last().Text
.RemoveThinkTags(
                    );

                    _notifier.StatusMessage($"Iteration #{iterationCount}: Received execution‐error fix from AI");
                    sqlQuery = lastAssistantFix;
                    result = await database.ExecuteSQLAsync(lastAssistantFix);
                    _notifier.StatusMessage(
                        $"Iteration #{iterationCount}: Result after fix: " +
                        $"{(result.Length > 100 ? result.Substring(0, 100) + "..." : result)}"
                    );
                }

                //
                // 4) Validate result with AI
                //
                _notifier.StatusMessage($"Iteration #{iterationCount}: AI result‐validation");
                var vp = $@"
User intent: {userIntent}
Executed SQL:
{sqlQuery}
Result (first 500 chars):
{(result.Length > 500 ? result.Substring(0, 500) + "..." : result)}
Please confirm columns, row‐count, and sanity.
If OK, respond NO_CHANGE; otherwise output only a revised SQL.";
                var vConvo = new List<ChatMessage> { systemMessage };
                if (lastAssistantFix != null) vConvo.Add(new ChatMessage(ChatRole.Assistant, lastAssistantFix));
                vConvo.Add(new ChatMessage(ChatRole.User, vp));

                var vs = (await _t2sqlClient.GetResponseAsync(vConvo, _t2sqlChatOptions))
                    .Messages.Last(m => m.Role == ChatRole.Assistant)
                    .Text
                    .Trim()
.RemoveThinkTags(
                );

                if (vs.Equals("NO_CHANGE", StringComparison.OrdinalIgnoreCase))
                {
                    _notifier.StatusMessage($"Iteration #{iterationCount}: Validation OK – returning result");
                    return result;
                }

                _notifier.StatusMessage($"Iteration #{iterationCount}: AI requested tweak – looping again");
                lastAssistantFix = vs;
                sqlQuery = vs;
            }
        }






        public async Task<string> ImproveAndExecuteSQLQueryOld(
           IDatabase database,
           string userIntent,
           string query,
           Dictionary<string, TableSchema> tableSchema)
        {

            //log here about additional message passes
            try
            {
                var assistantModelId = _currentConfig.ModelOptions
          .First(m => m.Key == "Assistant").Options.ModelId;

                string correctionModelId = _currentConfig.ModelOptions
                    .FirstOrDefault(m => m.Key == "Correction")?.Options.ModelId
                    ?? assistantModelId;

                string text2SqlModelId = _currentConfig.ModelOptions
                    .FirstOrDefault(m => m.Key == "Text2SQL")?.Options.ModelId
                    ?? assistantModelId;
                // 0) prepare correction client & system prompt
                _t2sqlClient = _chatClientFactory(text2SqlModelId);
                const string correctionSystemPrompt =
                    "You are an SQL assistant. Given schema info and errors, output only corrected SQL queries.";

                var messages = new List<ChatMessage>
                {
                    new ChatMessage(ChatRole.System, correctionSystemPrompt)
                };

                string sqlQuery = query;
                bool correctedQuery = false;
                TableSchema individualSchema = null;

                while (!correctedQuery)
                {
                    // 1) parse & collect schema errors
                    var parser = new TSql150Parser(false);
                    TSqlFragment frag;
                    using (var rdr = new StringReader(sqlQuery))
                        frag = parser.Parse(rdr, out var parseErrors);

                    var visitor = new TableColumnVisitor();
                    frag.Accept(visitor);


                    var schemaErrors = new List<string>();
                    foreach (var colRef in visitor.ColumnReferences)
                    {
                        if (!tableSchema.TryGetValue(colRef.Table, out individualSchema))
                        {
                            schemaErrors.Add($"Table '{colRef.Table}' not found in provided schema.");
                            continue;
                        }


                        var actualFields = individualSchema.Fields.Select(f => f.FieldName).ToList();
                        if (!actualFields.Contains(colRef.Column, StringComparer.OrdinalIgnoreCase))
                        {
                            schemaErrors.Add(
                                $"Field '{colRef.Column}' not found on '{colRef.Table}'. " +
                                $"Available: {string.Join(", ", actualFields)}"
                            );
                        }
                    }

                    if (schemaErrors.Any())
                    {
                        // ask AI to fix schema issues
                        var errorBlock = string.Join("\n", schemaErrors);
                        var prompt = $@"
The SQL has these schema errors:
{errorBlock}

Please output only a corrected SQL query that fixes them.";


                        messages.Add(new ChatMessage(ChatRole.User, prompt));

                        Console.WriteLine($"Schema errors found in the validation SQL process: {errorBlock}");


                        var responseNew = await _t2sqlClient.GetResponseAsync(messages, _t2sqlChatOptions);
                        var aiFix = responseNew
                            .Messages
                            .Where(x => x.Role == ChatRole.Assistant)
                            .LastOrDefault()?
                            .Text?
                            .Trim()
                            ?? string.Empty;

                        sqlQuery = aiFix;
                        //messages.Add(responseNew.Messages);
                        messages.Add(new ChatMessage(ChatRole.Assistant, aiFix));
                        continue; // re-validate schema
                    }

                    // 2) ask AI to validate/improve filters & selected columns
                    var aiPrompt = $@"
User intent: {userIntent}
Current SQL:{sqlQuery}
Schema info: {JsonConvert.SerializeObject(individualSchema)}    


Please confirm this query:
1) uses only necessary filters for the intent,
2) selects the correct columns,
3) does not over- or under-filter.
If no changes are needed, respond exactly with: NO_CHANGE
Otherwise output ONLY the corrected SQL.";

                    Console.WriteLine("Additional Validation For Text To SQL Passed");
                    Console.WriteLine($"Performing Filter Validation With LLM {JsonConvert.SerializeObject(messages)}");
                    Console.WriteLine($"MSGOVER \n");

                    messages.Add(new ChatMessage(ChatRole.User, aiPrompt));

                    var response = await _t2sqlClient.GetResponseAsync(messages, _t2sqlChatOptions);
                    var suggestion = response
                        .Messages
                        .Where(x => x.Role == ChatRole.Assistant)
                        .LastOrDefault()?
                        .Text?
                        .Trim()
                        ?? string.Empty;

                    if (suggestion.Equals("NO_CHANGE", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("QUERY MARKED AS CORRECT WITH NO CHANGES NEEDED NO_CHANGE");
                        correctedQuery = true;
                    }
                    else
                    {
                        sqlQuery = suggestion;
                        messages.Add(new ChatMessage(ChatRole.Assistant, suggestion));
                        // loop back to ensure no new schema issues
                    }
                }

                while (true)
                {
                    Console.WriteLine($"In additional Validation >> Executing SQL Query: {sqlQuery} The Original Query Prior To Validation Changes Was {query}");

                    var result = await database.ExecuteSQLAsync(sqlQuery);

                    // assume errors come back as "<error .../>"
                    if (result.StartsWith("<error", StringComparison.OrdinalIgnoreCase))
                    {
                        // feed that error + schema back to AI
                        var execPrompt = $@"
Execution returned an error payload:
{result}
Schema info: {JsonConvert.SerializeObject(tableSchema)}

Please output only a corrected SQL query that fixes this error.";
                        messages.Add(new ChatMessage(ChatRole.User, execPrompt));

                        var execResponse = await _t2sqlClient.GetResponseAsync(messages, _t2sqlChatOptions);
                        var execFix = execResponse
                            .Messages
                            .Where(x => x.Role == ChatRole.Assistant)
                            .LastOrDefault()?
                            .Text?
                            .Trim()
                            ?? string.Empty;

                        sqlQuery = execFix;
                        messages.Add(new ChatMessage(ChatRole.Assistant, execFix));

                        // loop back through schema & validation
                        break;
                    }

                        // no error marker, we’re done
                        return result;
                }



            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                throw;
            }
            return null;
        }




                [Description("Gets known databases")]
        public async Task<string[]> GetKnownDatabases(string anyparam = null)
        {

            return databaseDict.Keys.ToArray();
        }


    }
}
