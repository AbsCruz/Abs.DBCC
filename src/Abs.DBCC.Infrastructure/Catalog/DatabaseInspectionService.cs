using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Inspection;

namespace Abs.DBCC.Infrastructure.Catalog;

public sealed class DatabaseInspectionService(
    ICollationCatalogService catalogService,
    ISqlScriptRunnerFactory runnerFactory) : IDatabaseInspectionService
{
    private const string ColumnsQuery = """
        SELECT
            s.name AS SchemaName,
            t.name AS TableName,
            c.name AS ColumnName,
            ty.name AS SqlDataType,
            c.collation_name AS CollationName
        FROM sys.tables t
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.columns c ON c.object_id = t.object_id
        JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE t.is_ms_shipped = 0
        ORDER BY s.name, t.name, c.column_id
        """;

    public async Task<DatabaseCollationReport> BuildCollationReportAsync(ConnectionProfile profile, CancellationToken ct = default)
    {
        var databaseCollation = await catalogService.GetDatabaseDefaultCollationAsync(profile, ct);

        await using var runner = await runnerFactory.CreateAsync(profile, ct);
        var rows = await runner.ExecuteQueryAsync(ColumnsQuery, ct: ct);

        var tables = rows
            .GroupBy(row => ((string)row["SchemaName"]!, (string)row["TableName"]!))
            .Select(group => new TableCollationReport(
                group.Key.Item1,
                group.Key.Item2,
                group.Select(MapColumn).ToList()))
            .ToList();

        return new DatabaseCollationReport(databaseCollation, tables);
    }

    private static ColumnCollationState MapColumn(IReadOnlyDictionary<string, object?> row)
    {
        var collationName = row["CollationName"] as string;

        return new ColumnCollationState(
            (string)row["SchemaName"]!,
            (string)row["TableName"]!,
            (string)row["ColumnName"]!,
            (string)row["SqlDataType"]!,
            IsCharacterType: collationName is not null,
            Collation: collationName is null ? null : new SqlCollationName(collationName));
    }
}
