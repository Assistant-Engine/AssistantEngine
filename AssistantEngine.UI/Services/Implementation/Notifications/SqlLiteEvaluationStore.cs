using AssistantEngine.UI.Services.Notifications;
using Dapper;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssistantEngine.UI.Services.Implementation.Notifications
{
    public sealed class SqlLiteEvaluationStore : IEvaluationStore
    {
        private readonly string _cs;
        public SqlLiteEvaluationStore(string connectionString) => _cs = connectionString;

        public async Task<string> SaveAsync(ScheduledEvaluation e, CancellationToken ct = default)
        {
            try
            {
                e.NextCheckUtc ??= e.DueUtc ?? DateTimeOffset.UtcNow;
                using var cn = new SqliteConnection(_cs);
                const string sql = @"
INSERT INTO Evaluations
 (Id,Instruction,ModelConfigId,DueUtc,IntervalSeconds,Repeat,State,NextCheckUtc,LastCheckUtc,ScratchpadJson,ExpiresUtc)
VALUES
 (@Id,@Instruction,@ModelConfigId,@DueUtc,@IntervalSeconds,@Repeat,@State,@NextCheckUtc,@LastCheckUtc,@ScratchpadJson,@ExpiresUtc)
ON CONFLICT(Id) DO UPDATE SET
  Instruction=excluded.Instruction,
  ModelConfigId=excluded.ModelConfigId,
  DueUtc=excluded.DueUtc,
  IntervalSeconds=excluded.IntervalSeconds,
  Repeat=excluded.Repeat,
  State=excluded.State,
  NextCheckUtc=excluded.NextCheckUtc,
  LastCheckUtc=excluded.LastCheckUtc,
  ScratchpadJson=excluded.ScratchpadJson,
  ExpiresUtc=excluded.ExpiresUtc;";
                await cn.ExecuteAsync(new CommandDefinition(sql, e, cancellationToken: ct));
                return e.Id;
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
            }
            return null;
        }
        public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
        {
            using var cn = new SqliteConnection(_cs);
            var sql = "DELETE FROM Evaluations WHERE Id = @id";
            var n = await cn.ExecuteAsync(new CommandDefinition(sql, new { id }, cancellationToken: ct));
            return n > 0;
        }

        public async Task<ScheduledEvaluation?> GetAsync(string id, CancellationToken ct = default)
        {
            using var cn = new SqliteConnection(_cs);
            var sql = "SELECT * FROM Evaluations WHERE Id=@id LIMIT 1";

            return await cn.QueryFirstOrDefaultAsync<ScheduledEvaluation>(
                new CommandDefinition(sql, new { id }, cancellationToken: ct));
        }

        public async IAsyncEnumerable<ScheduledEvaluation> DueAsync(
            DateTimeOffset nowUtc, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            using var cn = new SqliteConnection(_cs);
            var sql = @"SELECT * FROM Evaluations
                    WHERE State = @pending AND NextCheckUtc <= @now";
            var rows = await cn.QueryAsync<ScheduledEvaluation>(
                new CommandDefinition(sql, new { pending = (int)EvalState.Pending, now = nowUtc }, cancellationToken: ct));
            foreach (var r in rows)
            {
                if (ct.IsCancellationRequested) yield break;
                yield return r;
            }
        }

        public Task UpdateAsync(ScheduledEvaluation e, CancellationToken ct = default)
            => SaveAsync(e, ct);

        public async Task<IEnumerable<ScheduledEvaluation>> ListAsync(CancellationToken ct = default)
        {
            using var cn = new SqliteConnection(_cs);
            var sql = "SELECT * FROM Evaluations";
            return await cn.QueryAsync<ScheduledEvaluation>(new CommandDefinition(sql, cancellationToken: ct));
        }

        public async Task<bool> CancelAsync(string id, CancellationToken ct = default)
        {
            using var cn = new SqliteConnection(_cs);
            var sql = "UPDATE Evaluations SET State = @disabled WHERE Id = @id";
            var n = await cn.ExecuteAsync(new CommandDefinition(sql, new { id, disabled = (int)EvalState.Disabled }, cancellationToken: ct));
            return n > 0;
        }
    }

}
