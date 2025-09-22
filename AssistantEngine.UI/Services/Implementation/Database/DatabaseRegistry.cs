
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssistantEngine.UI.Services.Implementation.Database
{
    public interface IDatabaseRegistry
    {
        IReadOnlyDictionary<string, IDatabase> All { get; }
        IDatabase? Get(string id);
        void Register(DatabaseConfiguration config);
        void Remove(string id); 
        event Action<IDatabase> DatabaseAdded;
        event Action<string> DatabaseRemoved;
    }

    public class DatabaseRegistry : IDatabaseRegistry
    {
        public event Action<IDatabase>? DatabaseAdded;
        public event Action<string>? DatabaseRemoved;
        private readonly Dictionary<string, IDatabase> _databases = new();
       
        
        public bool DatabasesDisabled = false;
      
        public IReadOnlyDictionary<string, IDatabase> All => _databases;

        public IDatabase? Get(string id) =>
            _databases.TryGetValue(id, out var db) ? db : null;

        public void Register(DatabaseConfiguration config)
        {
            if (string.IsNullOrWhiteSpace(config.ConnectionString))
            {
                Console.WriteLine($"Skipping DB {config.Id}: no connection string.");
                return;
            }

            // if DB with same Id exists → overwrite
            try
            {
                var db = new GenericDatabase(config); //in the constructor it will try to connect

                _databases[config.Id] = db;

                DatabaseAdded?.Invoke(db); // can also mean "updated"
            }
            catch(SqlException ex)
            {
                DatabasesDisabled = true;
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
            }
         
        }

        public void Remove(string id)
        {
            if (_databases.Remove(id))
            {
                DatabaseRemoved?.Invoke(id);  // 👈 raise event
            }
        }
    }

}
