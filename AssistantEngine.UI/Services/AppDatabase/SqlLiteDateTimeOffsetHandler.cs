// Infrastructure/SqliteDateTimeOffsetHandler.cs
using System.Data;
using System.Globalization;
using Dapper;


namespace AssistantEngine.UI.Services.AppDatabase
{

    public sealed class SqliteDateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
            => parameter.Value = value.ToUniversalTime().ToString("o"); // ISO 8601

        public override DateTimeOffset Parse(object value) => value switch
        {
            string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            DateTime d => new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)),
            long ms => DateTimeOffset.FromUnixTimeMilliseconds(ms),
            _ => DateTimeOffset.Parse(value.ToString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
        };
    }

}
