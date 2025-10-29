using Dapper;
using Microsoft.Data.Sqlite;

namespace AssistantEngine.UI.Services.AppDatabase;

public static class AppDbInitializer
{
    public static async Task EnsureSchemaAsync(string cs)
    {
        using var cn = new SqliteConnection(cs);
        await cn.OpenAsync();

        using (var cmd = cn.CreateCommand()) { cmd.CommandText = "PRAGMA journal_mode=WAL;"; await cmd.ExecuteNonQueryAsync(); }
        using (var cmd = cn.CreateCommand()) { cmd.CommandText = "PRAGMA foreign_keys=ON;"; await cmd.ExecuteNonQueryAsync(); }

        const string sql = @"
CREATE TABLE IF NOT EXISTS Evaluations(
  Id TEXT PRIMARY KEY,
  Instruction TEXT NOT NULL,
  ModelConfigId TEXT NOT NULL,
  DueUtc TEXT NULL,
  IntervalSeconds INTEGER NULL,
  Repeat INTEGER NOT NULL,
  State INTEGER NOT NULL,
  NextCheckUtc TEXT NULL,
  LastCheckUtc TEXT NULL,
  ScratchpadJson TEXT NULL,
  ExpiresUtc TEXT NULL
);
CREATE INDEX IF NOT EXISTS IX_Evaluations_Due ON Evaluations(State, NextCheckUtc);";
        await cn.ExecuteAsync(sql);
    }
}
