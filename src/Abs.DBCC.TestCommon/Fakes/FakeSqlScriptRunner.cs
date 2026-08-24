using System.Data;
using Abs.DBCC.Application.Ports;

namespace Abs.DBCC.TestCommon.Fakes;

/// <summary>
/// Records every SQL statement it was asked to execute and returns pre-scripted results,
/// so Application/Infrastructure logic can be unit-tested without a real database.
/// </summary>
public sealed class FakeSqlScriptRunner : ISqlScriptRunner
{
    private readonly Queue<IReadOnlyList<IReadOnlyDictionary<string, object?>>> _queryResults = new();
    private readonly Queue<object?> _scalarResults = new();
    private int _nonQueryCallCount;
    private int _beginTransactionCallCount;
    private int _commitCallCount;

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

    /// <summary>When set, the Nth call (1-based) to BeginTransactionAsync throws <see cref="ThrowOnTransactionBoundary"/> (or an OperationCanceledException).</summary>
    public int? FailOnBeginTransactionCallNumber { get; set; }

    /// <summary>When set, the Nth call (1-based) to CommitAsync throws <see cref="ThrowOnTransactionBoundary"/> (or an OperationCanceledException).</summary>
    public int? FailOnCommitCallNumber { get; set; }

    /// <summary>
    /// Exception thrown by the matching Fail-On-*-CallNumber above; deliberately separate from
    /// <see cref="ThrowOnExecute"/> (which, once set, throws unconditionally from every ExecuteXxxAsync
    /// call when no specific FailOnNonQueryCallNumber is set - combining the two would make an
    /// unrelated step fail too instead of just the targeted transaction boundary call).
    /// </summary>
    public Exception? ThrowOnTransactionBoundary { get; set; }

    public void EnqueueQueryResult(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) =>
        _queryResults.Enqueue(rows);

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

    public IAsyncEnumerable<IReadOnlyDictionary<string, object?>> StreamQueryAsync(
        string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
    {
        ExecutedSql.Add(sql);
        if (ThrowOnExecute is not null)
            throw ThrowOnExecute;

        var result = _queryResults.Count > 0 ? _queryResults.Dequeue() : Array.Empty<IReadOnlyDictionary<string, object?>>();
        return StreamAsync(result, ct);
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> StreamAsync(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            yield return row;
            await Task.Yield();
        }
    }

    public Task<int> ExecuteNonQueryAsync(
        string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
    {
        ExecutedSql.Add(sql);
        _nonQueryCallCount++;

        if (FailOnNonQueryCallNumber == _nonQueryCallCount)
            throw ThrowOnExecute ?? new InvalidOperationException($"Simulated failure on non-query call {_nonQueryCallCount}.");
        if (FailOnNonQueryCallNumber is null && ThrowOnExecute is not null)
            throw ThrowOnExecute;

        return Task.FromResult(0);
    }

    public Task<T?> ExecuteScalarAsync<T>(
        string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
    {
        ExecutedSql.Add(sql);
        if (ThrowOnExecute is not null)
            throw ThrowOnExecute;

        // Casting a boxed null straight to an unconstrained T? throws NullReferenceException instead of
        // just producing default(T?) - returning early here avoids ever attempting that cast.
        if (_scalarResults.Count == 0)
            return Task.FromResult<T?>(default);

        return Task.FromResult((T?)_scalarResults.Dequeue());
    }

    public IsolationLevel? LastRequestedIsolationLevel { get; private set; }

    public Task BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.Unspecified, CancellationToken ct = default)
    {
        LastRequestedIsolationLevel = isolationLevel;
        _beginTransactionCallCount++;
        if (FailOnBeginTransactionCallNumber == _beginTransactionCallCount)
            throw ThrowOnTransactionBoundary ?? new OperationCanceledException();

        IsInTransaction = true;
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken ct = default)
    {
        _commitCallCount++;
        if (FailOnCommitCallNumber == _commitCallCount)
            throw ThrowOnTransactionBoundary ?? new OperationCanceledException();

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
