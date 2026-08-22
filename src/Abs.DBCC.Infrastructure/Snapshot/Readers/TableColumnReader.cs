using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Snapshot.Readers;

/// <summary>
/// Reads sys.tables/sys.columns/sys.types (left-joined to sys.computed_columns for the computed
/// column definition) and groups the rows per table.
/// </summary>
public sealed class TableColumnReader
{
    private const string Query = """
        SELECT
            s.name AS SchemaName,
            t.name AS TableName,
            c.column_id AS ColumnId,
            c.name AS ColumnName,
            ty.name AS SqlDataType,
            c.max_length AS MaxLength,
            c.precision AS Precision,
            c.scale AS Scale,
            c.is_nullable AS IsNullable,
            c.collation_name AS CollationName,
            c.is_computed AS IsComputed,
            cc.definition AS ComputedDefinition,
            cc.is_persisted AS IsComputedPersisted
        FROM sys.tables t
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.columns c ON c.object_id = t.object_id
        JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
        WHERE t.is_ms_shipped = 0
        ORDER BY s.name, t.name, c.column_id
        """;

    public async Task<IReadOnlyDictionary<ObjectRef, List<ColumnSnapshot>>> ReadAsync(ISqlScriptRunner runner, CancellationToken ct)
    {
        var rows = await runner.ExecuteQueryAsync(Query, ct: ct);
        var result = new Dictionary<ObjectRef, List<ColumnSnapshot>>();

        foreach (var row in rows)
        {
            var tableRef = new ObjectRef((string)row["SchemaName"]!, (string)row["TableName"]!, DatabaseObjectKind.Table);
            if (!result.TryGetValue(tableRef, out var columns))
                result[tableRef] = columns = [];

            columns.Add(MapColumn(row));
        }

        return result;
    }

    private static ColumnSnapshot MapColumn(IReadOnlyDictionary<string, object?> row)
    {
        var collationName = row["CollationName"] as string;

        return new ColumnSnapshot(
            Name: (string)row["ColumnName"]!,
            SqlDataType: (string)row["SqlDataType"]!,
            MaxLength: row["MaxLength"] is short maxLength ? (int?)maxLength : null,
            Precision: row["Precision"] as byte?,
            Scale: row["Scale"] as byte?,
            IsNullable: (bool)row["IsNullable"]!,
            Collation: collationName is null ? null : new SqlCollationName(collationName),
            IsComputed: (bool)row["IsComputed"]!,
            ComputedDefinition: row["ComputedDefinition"] as string,
            IsComputedPersisted: row["IsComputedPersisted"] as bool? ?? false);
    }
}
