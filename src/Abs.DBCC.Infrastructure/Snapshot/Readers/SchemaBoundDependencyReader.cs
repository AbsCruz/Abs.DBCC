using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Snapshot.Readers;

/// <summary>
/// Reads sys.sql_expression_dependencies for schema-bound (WITH SCHEMABINDING) views/functions,
/// resolved down to the exact table column each one references - these are precisely the objects
/// that block ALTER COLUMN on that column and must be dropped and recreated around it.
/// </summary>
public sealed class SchemaBoundDependencyReader
{
    private const string Query = """
        SELECT
            rs.name AS DependentSchema,
            ro.name AS DependentName,
            ro.type AS DependentTypeCode,
            ts.name AS ReferencedSchema,
            t.name AS ReferencedTable,
            c.name AS ReferencedColumn
        FROM sys.sql_expression_dependencies dep
        JOIN sys.objects ro ON ro.object_id = dep.referencing_id
        JOIN sys.schemas rs ON rs.schema_id = ro.schema_id
        JOIN sys.columns c ON c.object_id = dep.referenced_id AND c.column_id = dep.referenced_minor_id
        JOIN sys.tables t ON t.object_id = dep.referenced_id
        JOIN sys.schemas ts ON ts.schema_id = t.schema_id
        WHERE dep.referenced_minor_id > 0
          AND OBJECTPROPERTY(ro.object_id, 'IsSchemaBound') = 1
        ORDER BY rs.name, ro.name
        """;

    public async Task<List<SchemaBoundDependency>> ReadAsync(ISqlScriptRunner runner, CancellationToken ct)
    {
        var rows = await runner.ExecuteQueryAsync(Query, ct: ct);
        return rows.Select(MapRow).ToList();
    }

    private static SchemaBoundDependency MapRow(IReadOnlyDictionary<string, object?> row)
    {
        var kind = ((string)row["DependentTypeCode"]!).Trim() switch
        {
            "V" => DatabaseObjectKind.View,
            "FN" or "IF" or "TF" => DatabaseObjectKind.Function,
            var other => throw new InvalidOperationException($"Unexpected schema-bound object type '{other}'.")
        };

        var dependent = new ObjectRef((string)row["DependentSchema"]!, (string)row["DependentName"]!, kind);
        var referencedTable = new ObjectRef((string)row["ReferencedSchema"]!, (string)row["ReferencedTable"]!, DatabaseObjectKind.Table);

        return new SchemaBoundDependency(dependent, referencedTable, (string)row["ReferencedColumn"]!);
    }
}
