namespace AssistantEngine.UI.Services.Implementation.Database
{
    public class StoredProcedure
    {
        public string Schema { get; set; }
        public string Name { get; set; }
        public string FullName => $"[{Schema}].[{Name}]";
        public List<StoredProcParameter> Parameters { get; set; } = new();
    }
    public class StoredProcParameter
    {
        public string Name { get; set; }   // e.g. "@CustomerId"
        public string DataType { get; set; }   // e.g. "int", "nvarchar(50)"
        public bool IsOutput { get; set; }   // true for OUTPUT params
    }

}
