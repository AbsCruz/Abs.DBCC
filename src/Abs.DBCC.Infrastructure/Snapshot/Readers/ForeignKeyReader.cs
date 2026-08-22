using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Snapshot.Readers;

/// <summary>Reads sys.foreign_keys/sys.foreign_key_columns as a flat, database-wide list (a foreign key spans two tables).</summary>
public sealed class ForeignKeyReader
{
    private const string Query = """
        SELECT
            fk.name AS ForeignKeyName,
            ps.name AS ParentSchema,
            pt.name AS ParentTable,
            rs.name AS ReferencedSchema,
            rt.name AS ReferencedTable,
            fk.delete_referential_action_desc AS DeleteAction,
            fk.update_referential_action_desc AS UpdateAction,
            fk.is_not_for_replication AS IsNotForReplication,
            pc.name AS ParentColumn,
            rc.name AS ReferencedColumn,
            fkc.constraint_column_id AS ColumnOrdinal
        FROM sys.foreign_keys fk
        JOIN sys.tables pt ON pt.object_id = fk.parent_object_id
        JOIN sys.schemas ps ON ps.schema_id = pt.schema_id
        JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
        JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
        JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
        JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
        WHERE pt.is_ms_shipped = 0
        ORDER BY fk.name, fkc.constraint_column_id
        """;

    public async Task<List<ForeignKeySnapshot>> ReadAsync(ISqlScriptRunner runner, CancellationToken ct)
    {
        var rows = await runner.ExecuteQueryAsync(Query, ct: ct);
        var byName = new Dictionary<string, (ForeignKeyHeader Header, List<ForeignKeyColumnSnapshot> Columns)>();

        foreach (var row in rows)
        {
            var name = (string)row["ForeignKeyName"]!;
            if (!byName.TryGetValue(name, out var entry))
            {
                entry = (new ForeignKeyHeader(
                    new ObjectRef((string)row["ParentSchema"]!, (string)row["ParentTable"]!, DatabaseObjectKind.Table),
                    new ObjectRef((string)row["ReferencedSchema"]!, (string)row["ReferencedTable"]!, DatabaseObjectKind.Table),
                    (string)row["DeleteAction"]!,
                    (string)row["UpdateAction"]!,
                    (bool)row["IsNotForReplication"]!), []);
                byName[name] = entry;
            }

            entry.Columns.Add(new ForeignKeyColumnSnapshot((string)row["ParentColumn"]!, (string)row["ReferencedColumn"]!));
        }

        return byName
            .Select(kv => new ForeignKeySnapshot(
                kv.Key, kv.Value.Header.ParentTable, kv.Value.Header.ReferencedTable, kv.Value.Columns,
                kv.Value.Header.DeleteAction, kv.Value.Header.UpdateAction, kv.Value.Header.IsNotForReplication))
            .ToList();
    }

    private sealed record ForeignKeyHeader(
        ObjectRef ParentTable, ObjectRef ReferencedTable, string DeleteAction, string UpdateAction, bool IsNotForReplication);
}
