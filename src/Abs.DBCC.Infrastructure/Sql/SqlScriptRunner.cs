using System.Data;
using System.Runtime.CompilerServices;
using Abs.DBCC.Application.Ports;
using Microsoft.Data.SqlClient;

namespace Abs.DBCC.Infrastructure.Sql;

public sealed class SqlScriptRunner(SqlConnection connection) : ISqlScriptRunner
{
    private SqlTransaction? _transaction;

    public async Task BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.Unspecified, CancellationToken ct = default) =>
        // SqlConnection.BeginTransactionAsync(IsolationLevel, ...) rejects IsolationLevel.Unspecified
        // outright (it must be a concrete level) - the parameterless overload is the correct way to ask
        // for the connection's normal default instead.
        _transaction = (SqlTransaction)(isolationLevel == IsolationLevel.Unspecified
            ? await connection.BeginTransactionAsync(ct)
            : await connection.BeginTransactionAsync(isolationLevel, ct));

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

    public async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> StreamQueryAsync(
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

        if (parameters is not null)
        {
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    public async ValueTask DisposeAsync() => await connection.DisposeAsync();
}
