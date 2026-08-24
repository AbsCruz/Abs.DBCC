using System.Data;

namespace Abs.DBCC.Application.Ports;

/// <summary>
/// Thin abstraction over ADO.NET execution against one already-open database connection.
/// This is the seam that keeps everything built on top of it mockable in unit tests.
/// </summary>
public interface ISqlScriptRunner : IAsyncDisposable
{
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ExecuteQueryAsync(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default);

    /// <summary>
    /// Same query execution as <see cref="ExecuteQueryAsync"/>, but yields each row as it is read
    /// instead of buffering the whole result set into a list first - the seam
    /// <see cref="Abs.DBCC.Infrastructure.Verification.DataVerificationService"/> needs to verify a
    /// table with millions of rows without holding all of them in memory at once.
    /// </summary>
    IAsyncEnumerable<IReadOnlyDictionary<string, object?>> StreamQueryAsync(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default);

    Task<int> ExecuteNonQueryAsync(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default);

    Task<T?> ExecuteScalarAsync<T>(
        string sql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default);

    /// <summary>
    /// <paramref name="isolationLevel"/> defaults to the connection's normal behavior (SQL Server's
    /// READ COMMITTED). <see cref="Abs.DBCC.Infrastructure.Verification.DataVerificationService"/>
    /// requests <see cref="IsolationLevel.Snapshot"/> instead, when the database supports it, so that a
    /// multi-table before/after capture sees one consistent point in time - without it, concurrent
    /// writes from other sessions during the capture could be misreported as migration-caused changes.
    /// </summary>
    Task BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.Unspecified, CancellationToken ct = default);

    Task CommitAsync(CancellationToken ct = default);

    Task RollbackAsync(CancellationToken ct = default);
}
