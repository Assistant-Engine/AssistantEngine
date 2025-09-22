using AssistantEngine.Services.Implementation.Tools;
using AssistantEngine.UI.Services.Implementation.Database;
using AssistantEngine.UI.Services.Implementation.Factories;
using AssistantEngine.UI.Services.Models;
using Microsoft.Extensions.AI;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace AssistantEngine.UI.Services.Implementation.Tools.OldTools
{



    public class DatabaseToolX1
    {



        public static string RemoveThinkTags(string input)
        {
            // remove all <think>…</think> lines
            var noThink = Regex.Replace(
                input,
                @"(?ims)^[ \t]*<think>.*?</think>[ \t]*\r?\n?",
                string.Empty
            );

            // trim any remaining whitespace or blank lines at the ends
            return noThink.Trim();
        }



        ChatClientFactory _chatClientFactory;
        Dictionary<string, IDatabase> databaseDict;
        AssistantConfig _currentConfig;
        IChatClient _correctionClient;

        //so next what does this neeed to do? it needs to embed a description.
        //we can inject the model.
        private readonly SemanticSearch _search;
        public DatabaseToolX1(SemanticSearch search, IEnumerable<IDatabase> Databases, ChatClientFactory chatClientFactory, AssistantConfig config)
        {
            databaseDict = Databases.ToDictionary(db => db.Configuration.Id, db => db);
            _search = search;
            _chatClientFactory = chatClientFactory;
            _currentConfig = config;
        }

        [Description("Searches the SQL schema to find relevant information on tables to use for queries")]
        public async Task<IEnumerable<string>> SearchSchemaOLLLD(
        [Description("The database name")] string database,
        [Description("Search Phrase e.g. Live Market Data")] string searchPhrase
    )
        {
            Console.WriteLine($"Searching Database {database} for search phrase {searchPhrase}");


            var filters = new Dictionary<string, string>();
      
            ///   await InvokeAsync(StateHasChanged);
            var results = await _search.SearchAsync("sql-table-chunks", searchPhrase, filters, maxResults: 5);


            Console.WriteLine("Searching schema gave the following results");
            foreach(var result in results)
            {
                Console.WriteLine("Result: \n ");
                Console.WriteLine(JsonConvert.SerializeObject(result));
                Console.WriteLine("Result End \n ");

            }


            return results.Select(result =>
                $"<result filename=\"{result.DocumentId}\" page_number=\"1\">{result.Text}</result>");
        }



       /* [Description("Gets the SQL schema of all the tables in a database")]
        public async Task<Dictionary<string, TableSchema>> GetSQLSchema([Description("The database name")] string database)
        {
            return databaseDict[database].GetSqlSchema();
        }*/

        [Description("Executes an SQL query")]
        public async Task<string> ExecuteSQL([Description("The database name")] string database, [Description("The SQL query to execute")] string sqlQuery, [Description("The user intent")] string userIntent)
        {

            Console.WriteLine($"Executing SQL {sqlQuery} on database {database}");
            try
            {
             
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



                var result = await ImproveAndExecuteSQLQuery(db, userIntent, sqlQuery, schema);
                //var result = await db.ExecuteSQLAsync(sqlQuery);
                return result;
            }
            catch(Exception ex)
            {
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
            string correctionModelId = _currentConfig.ModelOptions.First(m => m.Key == "Correction").Options.ModelId;
            const int MaxIterations = 7;
            int iterationCount = 0;
            Console.WriteLine("Starting ImproveAndExecuteSQLQuery");

            // Prepare correction client & system prompt
            _correctionClient = _chatClientFactory(correctionModelId);
            var systemMessage = new ChatMessage(
                ChatRole.System,
                "You are an SQL assistant. Given schema info and errors, output only corrected SQL queries." //CHANGE THIS
              //  + (string.IsNullOrEmpty(_currentConfig.DatabaseConsiderations)? "" : $"Consider the additional requirements: {_currentConfig.DatabaseConsiderations}")
              //  + (string.IsNullOrEmpty(database.Dialect)? "" : $"The SQL dialect should be: {database.Dialect}")
            );

            string sqlQuery = query;
            string lastAssistantFix = null;
            TableSchema currentTableSchema = null ; 

            //schema errors should come after

            while (true)
            {
                iterationCount++;
                if (iterationCount > MaxIterations)
                    throw new InvalidOperationException(
                        $"Unable to converge after {MaxIterations} attempts."
                    );

                Console.WriteLine($"----- Iteration #{iterationCount} -----");
                Console.WriteLine("----- New iteration -----");
                Console.WriteLine($"Current SQL: {sqlQuery}");

                //
                // 1) Schema check
                //
                Console.WriteLine("Step 1: Schema check");
                var parser = new TSql150Parser(false);
                using var rdr = new StringReader(sqlQuery);
                var frag = parser.Parse(rdr, out var parseErrors);

                var visitor = new TableColumnVisitor();
                frag.Accept(visitor);

                var schemaErrors = new List<string>();
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
                    Console.WriteLine($"Schema errors detected ({schemaErrors.Count}):");
                    schemaErrors.ForEach(err => Console.WriteLine("  - " + err));

                    Console.WriteLine("Requesting schema correction from AI...");

                    var prompt = $@"
                        The SQL has these schema errors:
                        {string.Join("\n", schemaErrors)}
                        Please output only a corrected SQL query that fixes them.";

                    var convo = new List<ChatMessage> { systemMessage };
                    if (lastAssistantFix != null)
                        convo.Add(new ChatMessage(ChatRole.Assistant, lastAssistantFix));
                    convo.Add(new ChatMessage(ChatRole.User, prompt));

                    var resp = await _correctionClient.GetResponseAsync(convo);
                    lastAssistantFix = resp.Messages
                                         .Where(m => m.Role == ChatRole.Assistant)
                                         .Last()
                                         .Text
                                         .Trim();
                    Console.WriteLine("AI returned corrected SQL:");
                    Console.WriteLine(lastAssistantFix);

                    sqlQuery = lastAssistantFix;
                    continue;
                }
                else
                {
                    Console.WriteLine("Schema check passed, no errors found.");
                }

                    //
                    // 2) Filter & column‐validation
                    //
                    Console.WriteLine("Step 2: Filter & column validation");
                //removing - uses only necessary filters,
                //

                


                var fvPrompt = $@"
                    User intent: {userIntent}
                    Current SQL:
                    {sqlQuery}
                    Please confirm:
                    1) selects correct columns (and does not select too few columns, it is nice to give user additional info),
                    2) does not over-filter.
                    3) uses correct formats for fields, as per the example values.
              

                    If OK, respond exactly NO_CHANGE. Otherwise output only the corrected SQL.
                    
                    Here is the table schema {JsonConvert.SerializeObject(currentTableSchema)}

                    ";

                var fvConvo = new List<ChatMessage> { systemMessage };
                if (lastAssistantFix != null)
                    fvConvo.Add(new ChatMessage(ChatRole.Assistant, lastAssistantFix));
                fvConvo.Add(new ChatMessage(ChatRole.User, fvPrompt));

                Console.WriteLine("Requesting filter/column validation from AI...");
                var fvResp = await _correctionClient.GetResponseAsync(fvConvo);
                var suggestion = fvResp.Messages
                                    .Where(m => m.Role == ChatRole.Assistant)
                                    .Last()
                                    .Text
                                    .Trim();
                Console.WriteLine("AI suggested SQL change:");
            

                // strip any <think> tags before checking for NO_CHANGE
                var cleanSuggestion = RemoveThinkTags(suggestion);
                Console.WriteLine(cleanSuggestion);
                if (!cleanSuggestion.Equals("NO_CHANGE", StringComparison.OrdinalIgnoreCase))
                {
                    lastAssistantFix = cleanSuggestion;
                    sqlQuery = cleanSuggestion;
                    continue;
                }

                Console.WriteLine("No filter/column changes needed (AI responded NO_CHANGE).");

                Console.WriteLine("Step 3: Executing SQL");
                Console.WriteLine($"Executing SQL: {sqlQuery}");
                var result = await database.ExecuteSQLAsync(sqlQuery);
                Console.WriteLine($"Execution result: {(result.Length > 100 ? result.Substring(0, 100) + "..." : result)}");

                if (result.StartsWith("<error", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Execution error detected, requesting AI fix...");

                    var execPrompt = $@"
                        Execution returned an error:
                        {result}
                        This Table Schema (for reference): {JsonConvert.SerializeObject(currentTableSchema)}
                        Please output only a corrected SQL query that fixes this error.";

                    var exConvo = new List<ChatMessage> { systemMessage };
                    if (lastAssistantFix != null)
                        exConvo.Add(new ChatMessage(ChatRole.Assistant, lastAssistantFix));
                    exConvo.Add(new ChatMessage(ChatRole.User, execPrompt));

                    var exResp = await _correctionClient.GetResponseAsync(exConvo);
                    lastAssistantFix = RemoveThinkTags(exResp.Messages
                                         .Where(m => m.Role == ChatRole.Assistant)
                                         .Last()
                                         .Text);
                    Console.WriteLine("AI returned execution‐error fix:");
                    Console.WriteLine(lastAssistantFix);

                    // try executing the new query once more
                    Console.WriteLine("Re-executing fixed SQL...");
                    Console.WriteLine(lastAssistantFix);
                    result = await database.ExecuteSQLAsync(lastAssistantFix);
                    Console.WriteLine($"Result after fix: {(result.Length > 100 ? result.Substring(0, 100) + "..." : result)}");
                }


                Console.WriteLine("Step 4: AI result‐validation");
                var _validationClient = _correctionClient;//_chatClientFactory(_currentConfig.ValidationModelId);
                var vp = $@"
                    User intent: {userIntent}
                    Executed SQL:
                    {sqlQuery}
                    Result (first 500 chars):
                    {(result.Length > 500 ? result.Substring(0, 500) + "..." : result)}
                    Please confirm columns, row‑count, and sanity.
                    If OK, respond NO_CHANGE; otherwise output only a revised SQL.";

                var vConvo = new List<ChatMessage> { systemMessage };
                if (lastAssistantFix != null) vConvo.Add(new ChatMessage(ChatRole.Assistant, lastAssistantFix));
                vConvo.Add(new ChatMessage(ChatRole.User, vp));
                var vs = RemoveThinkTags((await _validationClient.GetResponseAsync(vConvo))
                            .Messages.Last(m => m.Role == ChatRole.Assistant)
                            .Text.Trim());

                if (vs.Equals("NO_CHANGE", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("AI happy with result.");
                    return result;
                }

                Console.WriteLine("AI requested tweak:");
                Console.WriteLine(vs);
                lastAssistantFix = vs;
                sqlQuery = vs;
                continue;   // sends you back to schema→filter→exec→validation
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
                string correctionModelId = _currentConfig.ModelOptions.First(m => m.Key == "Correction").Options.ModelId;
                // 0) prepare correction client & system prompt
                _correctionClient = _chatClientFactory(correctionModelId);
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


                        var responseNew = await _correctionClient.GetResponseAsync(messages);
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

                    var response = await _correctionClient.GetResponseAsync(messages);
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

                        var execResponse = await _correctionClient.GetResponseAsync(messages);
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



        /* public async Task<Dictionary<string, TableSchema>> DescribeSQLSchema(Dictionary<string, TableSchema> tableSchemaWithoutDescriptions)
         {
             //so we have the model, now we need to add the descriptions

         }*/
                /*[Description("Gets the Stored Procedures to perform actions in a database")]
                public async Task<Dictionary<string, StoredProcedure>> GetStoredProcedures([Description("The database name")] string database)
                {
                    var db = databaseDict[database];
                    var result = db.GetStoredProcedures();
                    return result;
                }*/

                [Description("Gets known databases")]
        public async Task<string[]> GetKnownDatabases(string anyparam = null)
        {

            return databaseDict.Keys.ToArray();
        }


    }
}
