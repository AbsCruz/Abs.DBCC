using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Collation;

namespace Abs.DBCC.Infrastructure.Catalog;

public sealed class CollationCatalogService(ISqlScriptRunnerFactory runnerFactory) : ICollationCatalogService
{
    public async Task<SqlCollationName> GetServerDefaultCollationAsync(ConnectionProfile profile, CancellationToken ct = default)
    {
        await using var runner = await runnerFactory.CreateAsync(profile, ct);
        var value = await runner.ExecuteScalarAsync<string>(
            "SELECT CAST(SERVERPROPERTY('Collation') AS nvarchar(128))", ct: ct);
        return new SqlCollationName(value ?? throw new InvalidOperationException("Server did not return a collation."));
    }

    public async Task<SqlCollationName> GetDatabaseDefaultCollationAsync(ConnectionProfile profile, CancellationToken ct = default)
    {
        await using var runner = await runnerFactory.CreateAsync(profile, ct);
        var value = await runner.ExecuteScalarAsync<string>(
            "SELECT CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128))", ct: ct);
        return new SqlCollationName(value ?? throw new InvalidOperationException("Database did not return a collation."));
    }

    public async Task<IReadOnlyList<CollationInfo>> GetAvailableCollationsAsync(ConnectionProfile profile, CancellationToken ct = default)
    {
        await using var runner = await runnerFactory.CreateAsync(profile, ct);
        var rows = await runner.ExecuteQueryAsync("SELECT name, description FROM sys.fn_helpcollations()", ct: ct);

        return rows
            .Select(row => new CollationInfo((string)row["name"]!, (string)row["description"]!))
            .ToList();
    }
}
