using AssistantEngine.Services.DataAccessLayer.AssistantEngine.DataAccessLayer;

using Newtonsoft.Json;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;
using System.Xml.Schema;


namespace AssistantEngine.UI.Services.Implementation.Database
{

    public abstract class Database : IDatabase
    {
        protected Database(DatabaseConfiguration config)
        {
            Configuration = config ?? throw new ArgumentNullException(nameof(config));
            DB = new DBManager(config.ConnectionString); // 👈 new instance per DB
            Initialise();
        }
        protected DBManager DB { get; }
        public bool isLoaded { get; set; } = false;

        public DatabaseConfiguration Configuration { get; set; }
        //public abstract string Name { get; set; }
        //public abstract string Id { get; set; }

        //public string Dialect { get; set; } = "MSSQL";
        //public virtual string Configuration { get; set; } = string.Empty;

        // protected abstract string ConnectionString { get; set; }
        public Dictionary<string, TableSchema> SQLSchema { get; set; }

        public bool SetConnectionString(string connectionString)
        {

            return true;
        }

        public bool Initialise()
        {
            try
            {
                GetSqlSchema();
            }
            catch (Exception ex) {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);

            }
            return true;
        }
        public string ExecuteSQL(string command)
        {
            int characterReturnLimit = 10000;
            try
            {
                var dataTable = DB.GetDataTable(command, CommandType.Text, null, IsolationLevel.ReadUncommitted, true);
                string result = JsonConvert.SerializeObject(dataTable);
                if (result.Length > characterReturnLimit)
                {
                    result = $"The result returned more than the limit of {characterReturnLimit} characters so was cut off! : {result.Substring(0, characterReturnLimit)}";
                }

                return result;
            }
            catch (Exception ex)
            {
                return $"<error message=\"{ex.Message}" + "ERROR: you are currently calling the sql function, THIS IS NOT VALID SQL!: E.g. SELECT TOP(10) * FROM AIO_Exchange_MarketData. WHERE IS YOUR SELECT STATEMENT? PLEASE INPUT VALID SQL\" />";
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
            }

        }
        public async Task<string> ExecuteSQLAsync(string sql)
        {
            // off-load the blocking ExecuteSQL to the thread-pool
            string json = await Task.Run(() => ExecuteSQL(sql));
            return json;
        }
        public Dictionary<string, TableSchema> GetSqlSchema()
        {
            try
            {
                if (isLoaded && (SQLSchema != null || SQLSchema.Count > 0))
                {

                    return SQLSchema;
                }
                else
                {
                    isLoaded = true;
                    SQLSchema = GetSqlSchemaFromDb();
                    return SQLSchema;
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine($"Error getting SQL Schema {ex.Message} {ex.StackTrace}");
            }
            return null;
        }


        private Dictionary<string, TableSchema> GetSqlSchemaFromDb()
        {
            var sqlSchema = new Dictionary<string, TableSchema>();
            try
            {
                // load embedded .sql
                var asm = Assembly.GetExecutingAssembly();
                var name = "AssistantEngine.UI.sql.GetTableSchemas.sql";    // <= your resource name
                string sql;
                using (var s = asm.GetManifestResourceStream(name)!)
                using (var r = new StreamReader(s))
                    sql = r.ReadToEnd();

                // now run exactly as before, but with CommandType.Text
                var dataTable = DB.GetDataTable(
                    sql,
                    CommandType.Text,
                    null,
                    IsolationLevel.ReadUncommitted,
                    false,
                    true,
                    200);
                if (dataTable.Rows.Count > 0)
                {
                    foreach (DataRow row in dataTable.Rows)
                    {
                        string tableName = row["TableName"]?.ToString() ?? string.Empty;
                        string columnName = row["ColumnName"]?.ToString() ?? string.Empty;
                        string dataType = row["DataType"]?.ToString() ?? string.Empty;
                        string? exampleValue = string.IsNullOrEmpty(row["ExampleValue"]?.ToString()) ? null : row["ExampleValue"].ToString();

                        if (!tableName.StartsWith("AIO_Exchange") || tableName.Contains("KEY") || tableName.Contains("MERGE") || tableName.Contains("deadlock") || tableName.Contains("temp"))
                        {
                            continue;
                        }

                        if (!sqlSchema.ContainsKey(tableName))
                        {
                            sqlSchema[tableName] = new TableSchema
                            {
                                TableName = tableName,
                                Fields = new List<TableSchema.FieldSchema>()
                            };
                        }

                        sqlSchema[tableName].Fields.Add(new TableSchema.FieldSchema
                        {
                            FieldName = columnName,
                            DataType = dataType,
                            ExampleValueStringConverted = exampleValue
                        });
                    }
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine("Unable To Get The SQL Schema for the db");
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);

            }

            
            return sqlSchema;
            // …your existing row-loop logic…
        }
        public Dictionary<string, StoredProcedure> GetStoredProcedures()
        {
                       return GetStoredProcedureInfo(null, null);
        }
        public Dictionary<string, StoredProcedure> GetStoredProcedureInfo(
      string? schemaFilter = null,
      string? namePattern = null
  )
        {
            const string sql = @"
    SELECT 
      SCHEMA_NAME(p.schema_id)  AS SchemaName,
      p.name                    AS ProcName,
      prm.name                  AS ParamName,
      TYPE_NAME(prm.user_type_id) AS ParamType,
      prm.is_output             AS IsOutput
    FROM sys.procedures AS p WITH(NOLOCK)
    LEFT JOIN sys.parameters AS prm WITH(NOLOCK)
      ON p.object_id = prm.object_id
    WHERE (@schema IS NULL OR SCHEMA_NAME(p.schema_id) = @schema)
      AND (@pattern IS NULL OR p.name LIKE @pattern)
    ORDER BY SchemaName, ProcName, prm.parameter_id;
    ";

            var parms = new[]
            {
        new SqlParameter("@schema",  SqlDbType.NVarChar,128) { Value = (object?)schemaFilter ?? DBNull.Value },
        new SqlParameter("@pattern", SqlDbType.NVarChar,128) { Value = (object?)namePattern  ?? DBNull.Value },
    };

            var dt = DB.GetDataTable(
                sql,
                CommandType.Text,
                parms,
                IsolationLevel.ReadUncommitted,
                false,
                true,
                200
            );

            var dict = new Dictionary<string, StoredProcedure>();
            foreach (DataRow r in dt.Rows)
            {
                var key = $"{r["SchemaName"]}.{r["ProcName"]}";
                if (!dict.TryGetValue(key, out var info))
                {
                    info = new StoredProcedure
                    {
                        Schema = (string)r["SchemaName"]!,
                        Name = (string)r["ProcName"]!
                    };
                    dict[key] = info;
                }

                if (r["ParamName"] != DBNull.Value)
                    info.Parameters.Add(new StoredProcParameter
                    {
                        Name = (string)r["ParamName"]!,
                        DataType = (string)r["ParamType"]!,
                        IsOutput = (bool)r["IsOutput"]
                    });
            }

            return dict;
        }

        public Dictionary<string, TableSchema> GetSqlSchemaOld()
        {
            var sqlSchema = new Dictionary<string, TableSchema>();
            var dataTable = DB.GetDataTable("GetTableSchemas", CommandType.StoredProcedure,null,IsolationLevel.ReadUncommitted,false,true,200);

            if (dataTable.Rows.Count > 0)
            {
                foreach (DataRow row in dataTable.Rows)
                {
                    string tableName = row["TableName"]?.ToString() ?? string.Empty;
                    string columnName = row["ColumnName"]?.ToString() ?? string.Empty;
                    string dataType = row["DataType"]?.ToString() ?? string.Empty;
                    string? exampleValue = string.IsNullOrEmpty(row["ExampleValue"]?.ToString()) ? null : row["ExampleValue"].ToString();

                    if (!tableName.StartsWith("AIO_Exchange") || tableName.Contains("KEY") || tableName.Contains("MERGE") || tableName.Contains("deadlock") || tableName.Contains("temp"))
                    {
                        continue;
                    }

                    if (!sqlSchema.ContainsKey(tableName))
                    {
                        sqlSchema[tableName] = new TableSchema
                        {
                            TableName = tableName,
                            Fields = new List<TableSchema.FieldSchema>()
                        };
                    }

                    sqlSchema[tableName].Fields.Add(new TableSchema.FieldSchema
                    {
                        FieldName = columnName,
                        DataType = dataType,
                        ExampleValueStringConverted = exampleValue
                    });
                }
            }

            return sqlSchema;
        }

    
    }
}
