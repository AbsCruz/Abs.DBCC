using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Snapshot.Readers;

/// <summary>
/// Reads sys.extended_properties for class 1 ("Object or Column" - tables, table columns including
/// computed columns, views, procedures, functions, triggers, AND constraints, since PK/UQ/CHECK/
/// DEFAULT/FK constraints are rows in sys.objects too with parent_object_id resolving to the owning
/// table) and class 7 ("Index" - covers indexes on both tables and indexed views).
/// </summary>
public sealed class ExtendedPropertyReader
{
    private const string ObjectOrColumnQuery = """
        SELECT
            s.name AS SchemaName,
            o.name AS ObjectName,
            o.type AS ObjectTypeCode,
            c.name AS ColumnName,
            ep.name AS PropertyName,
            CAST(ep.value AS nvarchar(max)) AS PropertyValue,
            ps.name AS ParentSchemaName,
            pt.name AS ParentTableName
        FROM sys.extended_properties ep
        JOIN sys.objects o ON o.object_id = ep.major_id
        JOIN sys.schemas s ON s.schema_id = o.schema_id
        LEFT JOIN sys.columns c ON c.object_id = ep.major_id AND c.column_id = ep.minor_id AND ep.minor_id > 0
        LEFT JOIN sys.tables pt ON pt.object_id = o.parent_object_id
        LEFT JOIN sys.schemas ps ON ps.schema_id = pt.schema_id
        WHERE ep.class = 1 AND o.is_ms_shipped = 0
        ORDER BY s.name, o.name, ep.name
        """;

    private const string IndexQuery = """
        SELECT
            s.name AS SchemaName,
            o.name AS ParentName,
            o.type AS ParentTypeCode,
            i.name AS IndexName,
            ep.name AS PropertyName,
            CAST(ep.value AS nvarchar(max)) AS PropertyValue
        FROM sys.extended_properties ep
        JOIN sys.indexes i ON i.object_id = ep.major_id AND i.index_id = ep.minor_id
        JOIN sys.objects o ON o.object_id = ep.major_id
        JOIN sys.schemas s ON s.schema_id = o.schema_id
        WHERE ep.class = 7
        ORDER BY s.name, o.name, i.name
        """;

    public async Task<List<ExtendedPropertySnapshot>> ReadAsync(ISqlScriptRunner runner, CancellationToken ct)
    {
        var result = new List<ExtendedPropertySnapshot>();

        foreach (var row in await runner.ExecuteQueryAsync(ObjectOrColumnQuery, ct: ct))
        {
            var kind = ObjectTypeMapper.TryMap((string)row["ObjectTypeCode"]!);
            if (kind is null)
                continue;

            var objectRef = new ObjectRef((string)row["SchemaName"]!, (string)row["ObjectName"]!, kind.Value);
            var parentTable = ObjectTypeMapper.IsConstraintKind(kind.Value) && row["ParentTableName"] is string parentName
                ? new ObjectRef((string)row["ParentSchemaName"]!, parentName, DatabaseObjectKind.Table)
                : null;

            result.Add(new ExtendedPropertySnapshot(
                objectRef, row["ColumnName"] as string, (string)row["PropertyName"]!, (string)row["PropertyValue"]!, parentTable));
        }

        foreach (var row in await runner.ExecuteQueryAsync(IndexQuery, ct: ct))
        {
            // sys.objects.type is char(2): a one-letter code like 'V' comes back space-padded as "V ".
            var parentKind = ((string)row["ParentTypeCode"]!).Trim() == "V" ? DatabaseObjectKind.View : DatabaseObjectKind.Table;
            var parentRef = new ObjectRef((string)row["SchemaName"]!, (string)row["ParentName"]!, parentKind);
            var indexRef = new ObjectRef((string)row["SchemaName"]!, (string)row["IndexName"]!, DatabaseObjectKind.Index);

            result.Add(new ExtendedPropertySnapshot(
                indexRef, null, (string)row["PropertyName"]!, (string)row["PropertyValue"]!, parentRef));
        }

        return result;
    }
}
