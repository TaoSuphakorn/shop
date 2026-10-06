using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Shop.Shared;

/// <summary>One connection and one transaction per scope, shared by every module's DbContext.</summary>
public sealed class UnitOfWork(IConfiguration configuration) : IAsyncDisposable, IDisposable
{
    private const int Deadlock = 1205, LockTimeout = 1222, MaxAttempts = 3;
    private readonly List<DbContext> _contexts = [];

    public SqlConnection Connection { get; } = new(configuration.GetConnectionString("Shop"));
    public SqlTransaction? Transaction { get; private set; }

    public void Enlist(DbContext context)
    {
        _contexts.Add(context);
        if (Transaction is not null) context.Database.UseTransaction(Transaction);
    }

    public Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken ct) =>
        ExecuteAsync(async c => { await work(c); return 0; }, ct);

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        if (Transaction is not null) return await work(ct);   // nested call joins the running transaction
        if (Connection.State != ConnectionState.Open) await Connection.OpenAsync(ct);

        for (var attempt = 1; ; attempt++)
        {
            Transaction = (SqlTransaction)await Connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            try
            {
                await using (var cmd = new SqlCommand("SET LOCK_TIMEOUT 5000", Connection, Transaction))
                    await cmd.ExecuteNonQueryAsync(ct);
                foreach (var context in _contexts) context.Database.UseTransaction(Transaction);
                var result = await work(ct);
                await Transaction.CommitAsync(ct);
                return result;
            }
            catch (Exception ex)
            {
                try { await Transaction.RollbackAsync(CancellationToken.None); } catch { /* already rolled back by the server */ }
                foreach (var context in _contexts) context.ChangeTracker.Clear();
                var sql = SqlError(ex);
                if (sql?.Number == Deadlock && attempt < MaxAttempts) continue;
                if (sql?.Number == LockTimeout) throw new ApiException(409, "request_in_progress");
                throw;
            }
            finally
            {
                foreach (var context in _contexts) context.Database.UseTransaction(null);
                await Transaction.DisposeAsync();
                Transaction = null;
            }
        }
    }

    public static SqlException? SqlError(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
            if (ex is SqlException sql) return sql;
        return null;
    }

    public ValueTask DisposeAsync() => Connection.DisposeAsync();
    public void Dispose() => Connection.Dispose();
}
