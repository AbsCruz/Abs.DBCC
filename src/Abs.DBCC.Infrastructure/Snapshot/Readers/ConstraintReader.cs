using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Snapshot.Readers;

/// <summary>Reads sys.check_constraints and sys.default_constraints, grouped per table.</summary>
public sealed class ConstraintReader
{
    private const string CheckConstraintsQuery = """
        SELECT
            s.name AS SchemaName,
            t.name AS TableName,
            cc.name AS ConstraintName,
            col.name AS ColumnName,
            cc.definition AS Definition
        FROM sys.check_constraints cc
        JOIN sys.tables t ON t.object_id = cc.parent_object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        LEFT JOIN sys.columns col ON col.object_id = cc.parent_object_id AND col.column_id = cc.parent_column_id
        WHERE t.is_ms_shipped = 0
        ORDER BY s.name, t.name, cc.name
        """;

    private const string DefaultConstraintsQuery = """
        SELECT
            s.name AS SchemaName,
            t.name AS TableName,
            dc.name AS ConstraintName,
            col.name AS ColumnName,
            dc.definition AS Definition
        FROM sys.default_constraints dc
        JOIN sys.tables t ON t.object_id = dc.parent_object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.columns col ON col.object_id = dc.parent_object_id AND col.column_id = dc.parent_column_id
        WHERE t.is_ms_shipped = 0
        ORDER BY s.name, t.name, dc.name
        """;

    public async Task<(
        IReadOnlyDictionary<ObjectRef, List<CheckConstraintSnapshot>> CheckConstraints,
        IReadOnlyDictionary<ObjectRef, List<DefaultConstraintSnapshot>> DefaultConstraints)>
        ReadAsync(ISqlScriptRunner runner, CancellationToken ct)
    {
        var checkRows = await runner.ExecuteQueryAsync(CheckConstraintsQuery, ct: ct);
        var checks = new Dictionary<ObjectRef, List<CheckConstraintSnapshot>>();
        foreach (var row in checkRows)
        {
            var tableRef = TableRef(row);
            if (!checks.TryGetValue(tableRef, out var list))
                checks[tableRef] = list = [];

            list.Add(new CheckConstraintSnapshot(
                (string)row["ConstraintName"]!, row["ColumnName"] as string, (string)row["Definition"]!));
        }

        var defaultRows = await runner.ExecuteQueryAsync(DefaultConstraintsQuery, ct: ct);
        var defaults = new Dictionary<ObjectRef, List<DefaultConstraintSnapshot>>();
        foreach (var row in defaultRows)
        {
            var tableRef = TableRef(row);
            if (!defaults.TryGetValue(tableRef, out var list))
                defaults[tableRef] = list = [];

            list.Add(new DefaultConstraintSnapshot(
                (string)row["ConstraintName"]!, (string)row["ColumnName"]!, (string)row["Definition"]!));
        }

        return (checks, defaults);
    }

    private static ObjectRef TableRef(IReadOnlyDictionary<string, object?> row) =>
        new((string)row["SchemaName"]!, (string)row["TableName"]!, DatabaseObjectKind.Table);
}
