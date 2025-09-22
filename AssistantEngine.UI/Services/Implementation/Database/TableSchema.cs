namespace AssistantEngine.UI.Services.Implementation.Database
{
    public class TableSchema
    {
        public string TableName { get; set; }

        public List<FieldSchema> Fields { get; set; }

        public class FieldSchema
        {
            public string FieldName { get; set; }

            public string DataType { get; set; }

            public string? ExampleValueStringConverted { get; set; }
        }
    }

}
