namespace AssistantEngine.Services.DataAccessLayer
{
    using global::AssistantEngine.DataAccessLayer;
    using System;
    using System.Data;
    using System.Data.SqlClient;
    using System.Reflection;
    using System.Security.AccessControl;
    using System.Xml.Linq;

    namespace AssistantEngine.DataAccessLayer
    {
        public class DBManager
        {
            private readonly DatabaseHandlerFactory dbFactory;
            private readonly IDatabaseHandler database;
            private readonly string providerName;
            private readonly string connectionString;

            public SqlConnection ConnectionToDb { get; private set; }

            public DBManager(string connectionString)
            {
                this.connectionString = connectionString;
                dbFactory = new DatabaseHandlerFactory(connectionString);
                database = dbFactory.CreateDatabase();
                providerName = dbFactory.GetProviderName();

                ConnectionToDb = new SqlConnection(connectionString);
                ConnectionToDb.Open();
            }

            public IDbConnection GetDatabaseConnection()
            {
                return database.CreateConnection();
            }

            public void CloseConnection(IDbConnection connection)
            {
                database.CloseConnection(connection);
            }

            public IDbDataParameter CreateParameter(string name, object value, DbType dbType)
            {
                return DataParameterManager.CreateParameter(providerName, name, value, dbType, ParameterDirection.Input);
            }

            public IDbDataParameter CreateParameter(string name, int size, object value, DbType dbType)
            {
                return DataParameterManager.CreateParameter(providerName, name, size, value, dbType, ParameterDirection.Input);
            }

            public IDbDataParameter CreateParameter(string name, int size, object value, DbType dbType, ParameterDirection direction)
            {
                return DataParameterManager.CreateParameter(providerName, name, size, value, dbType, direction);
            }

            public DataTable GetDataTable(
                string commandText,
                CommandType commandType,
                IDbDataParameter[] parameters = null,
                IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
                bool throwException = false,
                bool logError = false,
                int commandTimeoutSeconds = -1)
            {
                try
                {
                    using (var connection = database.CreateConnection())
                    {
                        connection.Open();
                        using (var transaction = connection.BeginTransaction(isolationLevel))
                        using (var command = database.CreateCommand(commandText, commandType, connection))
                        {
                            command.Transaction = transaction;

                            if (commandTimeoutSeconds > -1)
                                command.CommandTimeout = commandTimeoutSeconds;

                            if (parameters != null)
                            {
                                foreach (var parameter in parameters)
                                    command.Parameters.Add(parameter);
                            }

                            var dataset = new DataSet();
                            var dataAdapter = database.CreateAdapter(command);
                            dataAdapter.Fill(dataset);

                            transaction.Commit();
                            return dataset.Tables[0];
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (throwException) throw;
                    Console.WriteLine($"GetDataTable failed: {ex.Message}");
                    return null;
                }
            }

            public DataSet GetDataSet(string commandText, CommandType commandType, IDbDataParameter[] parameters = null)
            {
                using (var connection = database.CreateConnection())
                {
                    connection.Open();
                    using (var command = database.CreateCommand(commandText, commandType, connection))
                    {
                        if (parameters != null)
                        {
                            foreach (var parameter in parameters)
                                command.Parameters.Add(parameter);
                        }

                        var dataset = new DataSet();
                        var dataAdapter = database.CreateAdapter(command);
                        dataAdapter.Fill(dataset);
                        return dataset;
                    }
                }
            }

            public IDataReader GetDataReader(string commandText, CommandType commandType, IDbDataParameter[] parameters, out IDbConnection connection)
            {
                connection = database.CreateConnection();
                connection.Open();

                var command = database.CreateCommand(commandText, commandType, connection);
                if (parameters != null)
                {
                    foreach (var parameter in parameters)
                        command.Parameters.Add(parameter);
                }

                return command.ExecuteReader();
            }

            public void Delete(string commandText, CommandType commandType, IDbDataParameter[] parameters = null)
            {
                using (var connection = database.CreateConnection())
                {
                    connection.Open();
                    using (var command = database.CreateCommand(commandText, commandType, connection))
                    {
                        if (parameters != null)
                        {
                            foreach (var parameter in parameters)
                                command.Parameters.Add(parameter);
                        }

                        command.ExecuteNonQuery();
                    }
                }
            }

            public void Insert(string commandText, CommandType commandType, IDbDataParameter[] parameters)
            {
                using (var connection = database.CreateConnection())
                {
                    connection.Open();
                    using (var command = database.CreateCommand(commandText, commandType, connection))
                    {
                        if (parameters != null)
                        {
                            foreach (var parameter in parameters)
                                command.Parameters.Add(parameter);
                        }

                        command.ExecuteNonQuery();
                    }
                }
            }

            public int Insert(string commandText, CommandType commandType, IDbDataParameter[] parameters, out int lastId)
            {
                lastId = 0;
                using (var connection = database.CreateConnection())
                {
                    connection.Open();
                    using (var command = database.CreateCommand(commandText, commandType, connection))
                    {
                        if (parameters != null)
                        {
                            foreach (var parameter in parameters)
                                command.Parameters.Add(parameter);
                        }

                        object newId = command.ExecuteScalar();
                        lastId = Convert.ToInt32(newId);
                    }
                }
                return lastId;
            }

            public long Insert(string commandText, CommandType commandType, IDbDataParameter[] parameters, out long lastId)
            {
                lastId = 0;
                using (var connection = database.CreateConnection())
                {
                    connection.Open();
                    using (var command = database.CreateCommand(commandText, commandType, connection))
                    {
                        if (parameters != null)
                        {
                            foreach (var parameter in parameters)
                                command.Parameters.Add(parameter);
                        }

                        object newId = command.ExecuteScalar();
                        lastId = Convert.ToInt64(newId);
                    }
                }
                return lastId;
            }

            public void InsertWithTransaction(string commandText, CommandType commandType, IDbDataParameter[] parameters)
            {
                using (var connection = database.CreateConnection())
                {
                    connection.Open();
                    using (var transactionScope = connection.BeginTransaction())
                    using (var command = database.CreateCommand(commandText, commandType, connection))
                    {
                        if (parameters != null)
                        {
                            foreach (var parameter in parameters)
                                command.Parameters.Add(parameter);
                        }

                        try
                        {
                            command.Transaction = transactionScope;
                            command.ExecuteNonQuery();
                            transactionScope.Commit();
                        }
                        catch
                        {
                            transactionScope.Rollback();
                            throw;
                        }
                    }
                }
            }

            public void InsertWithTransaction(string commandText, CommandType commandType, IsolationLevel isolationLevel, IDbDataParameter[] parameters)
            {
                using (var connection = database.CreateConnection())
                {
                    connection.Open();
                    using (var transactionScope = connection.BeginTransaction(isolationLevel))
                    using (var command = database.CreateCommand(commandText, commandType, connection))
                    {
                        if (parameters != null)
                        {
                            foreach (var parameter in parameters)
                                command.Parameters.Add(parameter);
                        }

                        try
                        {
                            command.Transaction = transactionScope;
                            command.ExecuteNonQuery();
                            transactionScope.Commit();
                        }
                        catch
                        {
                            transactionScope.Rollback();
                            throw;
                        }
                    }
                }
            }

            public void Update(string commandText, CommandType commandType, IDbDataParameter[] parameters)
            {
                using (var connection = database.CreateConnection())
                {
                    connection.Open();
                    using (var command = database.CreateCommand(commandText, commandType, connection))
                    {
                        if (parameters != null)
                        {
                            foreach (var parameter in parameters)
                                command.Parameters.Add(parameter);
                        }

                        command.ExecuteNonQuery();
                    }
                }
            }

            public void UpdateWithTransaction(string commandText, CommandType commandType, IDbDataParameter[] parameters)
            {
                using (var connection = database.CreateConnection())
                {
                    connection.Open();
                    using (var transactionScope = connection.BeginTransaction())
                    using (var command = database.CreateCommand(commandText, commandType, connection))
                    {
                        if (parameters != null)
                        {
                            foreach (var parameter in parameters)
                                command.Parameters.Add(parameter);
                        }

                        try
                        {
                            command.Transaction = transactionScope;
                            command.ExecuteNonQuery();
                            transactionScope.Commit();
                        }
                        catch
                        {
                            transactionScope.Rollback();
                            throw;
                        }
                    }
                }
            }

            public void UpdateWithTransaction(string commandText, CommandType commandType, IsolationLevel isolationLevel, IDbDataParameter[] parameters)
            {
                using (var connection = database.CreateConnection())
                {
                    connection.Open();
                    using (var transactionScope = connection.BeginTransaction(isolationLevel))
                    using (var command = database.CreateCommand(commandText, commandType, connection))
                    {
                        if (parameters != null)
                        {
                            foreach (var parameter in parameters)
                                command.Parameters.Add(parameter);
                        }

                        try
                        {
                            command.Transaction = transactionScope;
                            command.ExecuteNonQuery();
                            transactionScope.Commit();
                        }
                        catch
                        {
                            transactionScope.Rollback();
                            throw;
                        }
                    }
                }
            }

            public object GetScalarValue(string commandText, CommandType commandType, IDbDataParameter[] parameters = null)
            {
                using (var connection = database.CreateConnection())
                {
                    connection.Open();
                    using (var command = database.CreateCommand(commandText, commandType, connection))
                    {
                        if (parameters != null)
                        {
                            foreach (var parameter in parameters)
                                command.Parameters.Add(parameter);
                        }

                        return command.ExecuteScalar();
                    }
                }
            }



        }
    }

}
