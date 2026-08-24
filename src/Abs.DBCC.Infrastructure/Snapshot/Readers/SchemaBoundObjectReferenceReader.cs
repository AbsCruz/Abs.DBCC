using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Snapshot.Readers;

/// <summary>
/// Reads sys.sql_expression_dependencies for schema-bound (WITH SCHEMABINDING) views/functions that
/// directly reference another view or function - e.g. an indexed wrapper view built WITH SCHEMABINDING
/// on top of another schema-bound view. <see cref="SchemaBoundDependencyReader"/> only resolves
/// dependencies down to a table column, so it cannot see this kind of view-on-view chain; this reader
/// fills that gap so the migration planner can drop/recreate such chains in dependency-safe order.
/// </summary>
public sealed class SchemaBoundObjectReferenceReader
{
    private const string Query = """
        SELECT
            rs.name AS DependentSchema,
            ro.name AS DependentName,
            ro.type AS DependentTypeCode,
            fs.name AS ReferencedSchema,
            fo.name AS ReferencedName,
            fo.type AS ReferencedTypeCode
        FROM sys.sql_expression_dependencies dep
        JOIN sys.objects ro ON ro.object_id = dep.referencing_id
        JOIN sys.schemas rs ON rs.schema_id = ro.schema_id
        JOIN sys.objects fo ON fo.object_id = dep.referenced_id
        JOIN sys.schemas fs ON fs.schema_id = fo.schema_id
        WHERE fo.type IN ('V', 'FN', 'IF', 'TF')
          AND ro.object_id <> fo.object_id
          AND OBJECTPROPERTY(ro.object_id, 'IsSchemaBound') = 1
        ORDER BY rs.name, ro.name
        """;

    public async Task<List<SchemaBoundObjectReference>> ReadAsync(ISqlScriptRunner runner, CancellationToken ct)
    {
        var rows = await runner.ExecuteQueryAsync(Query, ct: ct);
        return rows.Select(MapRow).ToList();
    }

    private static SchemaBoundObjectReference MapRow(IReadOnlyDictionary<string, object?> row)
    {
        var dependent = new ObjectRef((string)row["DependentSchema"]!, (string)row["DependentName"]!, MapKind((string)row["DependentTypeCode"]!));
        var referenced = new ObjectRef((string)row["ReferencedSchema"]!, (string)row["ReferencedName"]!, MapKind((string)row["ReferencedTypeCode"]!));

        return new SchemaBoundObjectReference(dependent, referenced);
    }

    private static DatabaseObjectKind MapKind(string typeCode) => typeCode.Trim() switch
    {
        "V" => DatabaseObjectKind.View,
        "FN" or "IF" or "TF" => DatabaseObjectKind.Function,
        var other => throw new InvalidOperationException($"Unexpected schema-bound object type '{other}'.")
    };
}
