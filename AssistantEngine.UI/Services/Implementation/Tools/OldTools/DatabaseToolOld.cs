using AssistantEngine.UI.Services.Implementation.Database;
using System.ComponentModel;

namespace AssistantEngine.UI.Services.Implementation.Tools.OldTools
{
    public class DatabaseToolOld
    {
        Dictionary<string, IDatabase> databaseDict;

        //so next what does this neeed to do? it needs to embed a description.
        //we can inject the model.
        public DatabaseToolOld(IEnumerable<IDatabase> Databases) => databaseDict = Databases.ToDictionary(db => db.Configuration.Id, db => db);

        [Description("Gets the SQL schema of all the tables in a database")]
        public async Task<Dictionary<string, TableSchema>> GetSQLSchema([Description("The database name")] string database)
        {
            return databaseDict[database].GetSqlSchema();
        }
        [Description("Executes an SQL query")]
        public async Task<string> ExecuteSQL([Description("The database name")] string database, [Description("The SQL query to execute")] string sqlQuery)
        {
            var db = databaseDict[database];
            var result = await db.ExecuteSQLAsync(sqlQuery);
            return result;
        }


        /* public async Task<Dictionary<string, TableSchema>> DescribeSQLSchema(Dictionary<string, TableSchema> tableSchemaWithoutDescriptions)
         {
             //so we have the model, now we need to add the descriptions

         }*/
        [Description("Gets the Stored Procedures to perform actions in a database")]
        public async Task<Dictionary<string, StoredProcedure>> GetStoredProcedures([Description("The database name")] string database)
        {
            var db = databaseDict[database];
            var result = db.GetStoredProcedures();
            return result;
        }

        [Description("Gets known databases")]
        public async Task<string[]> GetKnownDatabases(string anyparam = null)
        {

            return databaseDict.Keys.ToArray();
        }


    }
}
