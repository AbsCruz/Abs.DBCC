using System.Runtime.CompilerServices;
using Abs.DBCC.Application.Ports;

namespace Abs.DBCC.TestCommon.Fakes;

/// <summary>
/// Records every SQL statement it was asked to execute and returns pre-scripted results,
/// so Application/Infrastructure logic can be unit-tested without a real database.
/// </summary>
public sealed class FakeSqlScriptRunner : ISqlScriptRunner
{
    private readonly Queue<IReadOnlyList<IReadOnlyDictionary<string, object?>>> _queryResults = new();
    private readonly Queue<IReadOnlyList<IReadOnlyDictionary<string, object?>>> _streamResults = new();
    private readonly Queue<object?> _scalarResults = new();
    private int _nonQueryCallCount;

    public List<string> ExecutedSql { get; } = [];
    public bool IsDisposed { get; private set; }
    public bool IsInTransaction { get; private set; }
    public bool WasCommitted { get; private set; }
    public bool WasRolledBack { get; private set; }

    /// <summary>When set, an exception is thrown from every ExecuteXxxAsync call.</summary>
    public Exception? ThrowOnExecute { get; set; }

    /// <summary>When set, the Nth call (1-based) to ExecuteNonQueryAsync throws <see cref="ThrowOnExecute"/> (or a default exception).</summary>
    public int? FailOnNonQueryCallNumber { get; set; }

    /// <summary>When set, RollbackAsync throws this instead of rolling back - simulates the connection already being dead.</summary>
    public Exception? ThrowOnRollback { get; set; }

    public void EnqueueQueryResult(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) =>
        _queryResults.Enqueue(rows);

    public void EnqueueStreamResult(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) =>
        _streamResults.Enqueue(rows);

    public void EnqueueScalarResult(object? value) => _scalarResults.Enqueue(value);

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ExecuteQueryAsync(
        string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
    {
        ExecutedSql.Add(sql);
        if (ThrowOnExecute is not null)
            throw ThrowOnExecute;

        var result = _queryResults.Count > 0
            ? _queryResults.Dequeue()
            : Array.Empty<IReadOnlyDictionary<string, object?>>();
        return Task.FromResult(result);
    }

    public async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ExecuteQueryStreamAsync(
        string sql, IReadOnlyDictionary<string, object?>? parameters = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ExecutedSql.Add(sql);
        if (ThrowOnExecute is not null)
            throw ThrowOnExecute;

        var rows = _streamResults.Count > 0 ? _streamResults.Dequeue() : Array.Empty<IReadOnlyDictionary<string, object?>>();
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            yield return row;
        }

        await Task.CompletedTask;
    }

    public Task<int> ExecuteNonQueryAsync(
        string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
    {
        ExecutedSql.Add(sql);
        _nonQueryCallCount++;

        if (FailOnNonQueryCallNumber == _nonQueryCallCount)
            throw ThrowOnExecute ?? new InvalidOperationException($"Simulated failure on non-query call {_nonQueryCallCount}.");
        if (ThrowOnExecute is not null)
            throw ThrowOnExecute;

        return Task.FromResult(0);
    }

    public Task<T?> ExecuteScalarAsync<T>(
        string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
    {
        ExecutedSql.Add(sql);
        if (ThrowOnExecute is not null)
            throw ThrowOnExecute;

        var value = _scalarResults.Count > 0 ? _scalarResults.Dequeue() : default;
        return Task.FromResult((T?)value);
    }

    public Task BeginTransactionAsync(CancellationToken ct = default)
    {
        IsInTransaction = true;
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken ct = default)
    {
        IsInTransaction = false;
        WasCommitted = true;
        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken ct = default)
    {
        if (ThrowOnRollback is not null)
            throw ThrowOnRollback;

        IsInTransaction = false;
        WasRolledBack = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }
}
