using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Snapshot.Readers;

/// <summary>
/// Reads sys.sql_expression_dependencies for computed columns that directly reference a schema-bound
/// view or function (e.g. a computed column defined as "dbo.SomeFunction(...)"). Neither
/// <see cref="SchemaBoundDependencyReader"/> nor <see cref="SchemaBoundObjectReferenceReader"/> can see
/// this: a computed column is not itself schema-bound, and it is not a table-column reference either -
/// this reader fills that gap so the migration planner can order a computed column's drop/recreate
/// correctly around the schema-bound object it calls.
/// </summary>
public sealed class ComputedColumnObjectReferenceReader
{
    private const string Query = """
        SELECT
            ts.name AS TableSchema,
            t.name AS TableName,
            c.name AS ColumnName,
            fs.name AS ReferencedSchema,
            fo.name AS ReferencedName,
            fo.type AS ReferencedTypeCode
        FROM sys.sql_expression_dependencies dep
        JOIN sys.columns c ON c.object_id = dep.referencing_id AND c.column_id = dep.referencing_minor_id AND c.is_computed = 1
        JOIN sys.tables t ON t.object_id = dep.referencing_id
        JOIN sys.schemas ts ON ts.schema_id = t.schema_id
        JOIN sys.objects fo ON fo.object_id = dep.referenced_id
        JOIN sys.schemas fs ON fs.schema_id = fo.schema_id
        WHERE dep.referencing_minor_id > 0
          AND fo.type IN ('V', 'FN', 'IF', 'TF')
          AND OBJECTPROPERTY(fo.object_id, 'IsSchemaBound') = 1
        ORDER BY ts.name, t.name, c.name
        """;

    public async Task<List<ComputedColumnObjectReference>> ReadAsync(ISqlScriptRunner runner, CancellationToken ct)
    {
        var rows = await runner.ExecuteQueryAsync(Query, ct: ct);
        return rows.Select(MapRow).ToList();
    }

    private static ComputedColumnObjectReference MapRow(IReadOnlyDictionary<string, object?> row)
    {
        var table = new ObjectRef((string)row["TableSchema"]!, (string)row["TableName"]!, DatabaseObjectKind.Table);
        var referenced = new ObjectRef((string)row["ReferencedSchema"]!, (string)row["ReferencedName"]!, MapKind((string)row["ReferencedTypeCode"]!));

        return new ComputedColumnObjectReference(table, (string)row["ColumnName"]!, referenced);
    }

    private static DatabaseObjectKind MapKind(string typeCode) => typeCode.Trim() switch
    {
        "V" => DatabaseObjectKind.View,
        "FN" or "IF" or "TF" => DatabaseObjectKind.Function,
        var other => throw new InvalidOperationException($"Unexpected schema-bound object type '{other}'.")
    };
}
