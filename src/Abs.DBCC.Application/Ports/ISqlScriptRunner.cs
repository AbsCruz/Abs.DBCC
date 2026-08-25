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
    /// Same query semantics as <see cref="ExecuteQueryAsync"/>, but rows are yielded one at a time as they
    /// are read from the data reader instead of being buffered into a list first. Use this for queries whose
    /// result size scales with table row counts, so a large table doesn't need to fit in memory all at once.
    /// </summary>
    IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ExecuteQueryStreamAsync(
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

    Task BeginTransactionAsync(CancellationToken ct = default);

    Task CommitAsync(CancellationToken ct = default);

    Task RollbackAsync(CancellationToken ct = default);
}
