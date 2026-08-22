using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Snapshot.Readers;

/// <summary>
/// Reads sys.indexes/sys.index_columns for tables (plain indexes as well as PK/unique-constraint-backing
/// indexes) and for schema-bound views (indexed views) - both are plain sys.indexes rows keyed by their
/// parent object's object_id, whether that parent is a table (sys.objects.type = 'U') or a view ('V').
/// </summary>
public sealed class IndexReader
{
    private const string Query = """
        SELECT
            s.name AS SchemaName,
            o.name AS ParentName,
            o.type AS ParentTypeCode,
            i.name AS IndexName,
            i.is_unique AS IsUnique,
            i.type_desc AS TypeDesc,
            i.is_primary_key AS IsPrimaryKey,
            i.is_unique_constraint AS IsUniqueConstraint,
            i.filter_definition AS FilterDefinition,
            ic.is_descending_key AS IsDescendingKey,
            ic.is_included_column AS IsIncludedColumn,
            ic.key_ordinal AS KeyOrdinal,
            ic.index_column_id AS IndexColumnId,
            c.name AS ColumnName
        FROM sys.objects o
        JOIN sys.schemas s ON s.schema_id = o.schema_id
        JOIN sys.indexes i ON i.object_id = o.object_id
        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE o.type IN ('U', 'V') AND o.is_ms_shipped = 0 AND i.name IS NOT NULL
        ORDER BY s.name, o.name, i.name, ic.key_ordinal, ic.index_column_id
        """;

    public async Task<(IReadOnlyDictionary<ObjectRef, List<IndexSnapshot>> TableIndexes, List<ViewIndexSnapshot> ViewIndexes)> ReadAsync(
        ISqlScriptRunner runner, CancellationToken ct)
    {
        var rows = await runner.ExecuteQueryAsync(Query, ct: ct);
        var byParentAndIndex = new Dictionary<(ObjectRef Parent, string IndexName), (IndexSnapshotHeader Header, List<IndexColumnSnapshot> Columns)>();

        foreach (var row in rows)
        {
            // sys.objects.type is char(2): a one-letter code like 'V' comes back space-padded as "V ".
            var kind = ((string)row["ParentTypeCode"]!).Trim() == "V" ? DatabaseObjectKind.View : DatabaseObjectKind.Table;
            var parentRef = new ObjectRef((string)row["SchemaName"]!, (string)row["ParentName"]!, kind);
            var indexName = (string)row["IndexName"]!;
            var key = (parentRef, indexName);

            if (!byParentAndIndex.TryGetValue(key, out var entry))
            {
                entry = (new IndexSnapshotHeader(
                    indexName,
                    (bool)row["IsUnique"]!,
                    string.Equals((string)row["TypeDesc"]!, "CLUSTERED", StringComparison.OrdinalIgnoreCase),
                    (bool)row["IsPrimaryKey"]!,
                    (bool)row["IsUniqueConstraint"]!,
                    row["FilterDefinition"] as string), []);
                byParentAndIndex[key] = entry;
            }

            entry.Columns.Add(new IndexColumnSnapshot(
                (string)row["ColumnName"]!,
                (bool)row["IsDescendingKey"]!,
                (bool)row["IsIncludedColumn"]!));
        }

        var tableIndexes = new Dictionary<ObjectRef, List<IndexSnapshot>>();
        var viewIndexes = new List<ViewIndexSnapshot>();

        foreach (var ((parentRef, _), (header, columns)) in byParentAndIndex)
        {
            var index = new IndexSnapshot(
                header.Name, header.IsUnique, header.IsClustered, header.IsPrimaryKey, header.IsUniqueConstraint,
                columns, header.FilterDefinition);

            if (parentRef.Kind == DatabaseObjectKind.View)
            {
                viewIndexes.Add(new ViewIndexSnapshot(parentRef, index));
            }
            else
            {
                if (!tableIndexes.TryGetValue(parentRef, out var indexes))
                    tableIndexes[parentRef] = indexes = [];
                indexes.Add(index);
            }
        }

        return (tableIndexes, viewIndexes);
    }

    private sealed record IndexSnapshotHeader(
        string Name, bool IsUnique, bool IsClustered, bool IsPrimaryKey, bool IsUniqueConstraint, string? FilterDefinition);
}
