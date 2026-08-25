using System.Runtime.CompilerServices;
using Abs.DBCC.Application.Ports;
using Microsoft.Data.SqlClient;

namespace Abs.DBCC.Infrastructure.Sql;

public sealed class SqlScriptRunner(SqlConnection connection) : ISqlScriptRunner
{
    private SqlTransaction? _transaction;

    public async Task BeginTransactionAsync(CancellationToken ct = default) =>
        _transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException("No active transaction to commit.");

        await _transaction.CommitAsync(ct);
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException("No active transaction to roll back.");

        await _transaction.RollbackAsync(ct);
        _transaction = null;
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ExecuteQueryAsync(
        string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
    {
        await using var command = CreateCommand(sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.GetValue(i);
                row[reader.GetName(i)] = value is DBNull ? null : value;
            }

            rows.Add(row);
        }

        return rows;
    }

    public async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ExecuteQueryStreamAsync(
        string sql, IReadOnlyDictionary<string, object?>? parameters = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var command = CreateCommand(sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.GetValue(i);
                row[reader.GetName(i)] = value is DBNull ? null : value;
            }

            yield return row;
        }
    }

    public async Task<int> ExecuteNonQueryAsync(
        string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
    {
        await using var command = CreateCommand(sql, parameters);
        return await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<T?> ExecuteScalarAsync<T>(
        string sql, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken ct = default)
    {
        await using var command = CreateCommand(sql, parameters);
        var result = await command.ExecuteScalarAsync(ct);
        return result is null or DBNull ? default : (T)Convert.ChangeType(result, typeof(T));
    }

    private SqlCommand CreateCommand(string sql, IReadOnlyDictionary<string, object?>? parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = _transaction;

        // ADO.NET's default CommandTimeout is 30 seconds, which a large ALTER COLUMN (rewrites every row)
        // or a SELECT * over a big table (data verification) can easily exceed - that's exactly what
        // produced "Execution Timeout Expired" failures during real migrations. User-initiated cancellation
        // is already handled via the CancellationToken passed to each ExecuteXxxAsync call, so removing
        // SqlClient's own arbitrary cutoff (0 = wait indefinitely) doesn't remove the user's way out.
        command.CommandTimeout = 0;

        if (parameters is not null)
        {
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    public async ValueTask DisposeAsync() => await connection.DisposeAsync();
}
