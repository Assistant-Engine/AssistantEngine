using System.Configuration;

namespace AssistantEngine.DataAccessLayer
{
    public class DatabaseHandlerFactory
    {
        private ConnectionStringSettings connectionStringSettings; 
  
        public DatabaseHandlerFactory(string connectionString)
        {
   

            if (connectionStringSettings == null)
            {
                connectionStringSettings = new ConnectionStringSettings();
                connectionStringSettings.ProviderName = "System.Data.SqlClient";
                connectionStringSettings.ConnectionString = connectionString;
            }
        }

        public IDatabaseHandler CreateDatabase()
        {
            IDatabaseHandler database = null;

            switch (connectionStringSettings.ProviderName.ToLower())
            {
                case "system.data.sqlclient":
                    database = new SqlDataAccess(connectionStringSettings.ConnectionString);
                    break;

            }

            return database;
        }

        public string GetProviderName()
        {
            return connectionStringSettings.ProviderName;
        }
    }
}


