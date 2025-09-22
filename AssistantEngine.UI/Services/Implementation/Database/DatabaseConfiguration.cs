using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssistantEngine.UI.Services.Implementation.Database
{
    public class DatabaseConfiguration
    {
        public string Id { get; set; }
        public string Dialect { get; set; } = "MSSSQL";
        public string ConnectionString { get; set; }
        public string DatabaseConsiderations { get; set; }
        public bool DescribeDatabaseWithModel { get; set; } = true;
        public DatabaseConfiguration Clone() => (DatabaseConfiguration)MemberwiseClone();
    }
}
