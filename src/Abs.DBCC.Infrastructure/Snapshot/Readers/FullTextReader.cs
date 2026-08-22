using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Snapshot.Readers;

/// <summary>
/// Reads full-text catalogs (container only, never dropped/recreated) and full-text indexes
/// (dropped/recreated around a collation change on any of their columns, like a regular index).
/// </summary>
public sealed class FullTextReader
{
    private const string CatalogsQuery = """
        SELECT name AS CatalogName, is_default AS IsDefault
        FROM sys.fulltext_catalogs
        ORDER BY name
        """;

    private const string IndexesQuery = """
        SELECT
            s.name AS SchemaName,
            t.name AS TableName,
            cat.name AS CatalogName,
            ix.name AS KeyIndexName,
            fi.change_tracking_state_desc AS ChangeTracking,
            c.name AS ColumnName,
            fic.language_id AS LanguageId
        FROM sys.fulltext_indexes fi
        JOIN sys.tables t ON t.object_id = fi.object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.fulltext_catalogs cat ON cat.fulltext_catalog_id = fi.fulltext_catalog_id
        JOIN sys.indexes ix ON ix.object_id = fi.object_id AND ix.index_id = fi.unique_index_id
        JOIN sys.fulltext_index_columns fic ON fic.object_id = fi.object_id
        JOIN sys.columns c ON c.object_id = fic.object_id AND c.column_id = fic.column_id
        ORDER BY s.name, t.name, c.column_id
        """;

    public async Task<List<FullTextCatalogSnapshot>> ReadCatalogsAsync(ISqlScriptRunner runner, CancellationToken ct)
    {
        var rows = await runner.ExecuteQueryAsync(CatalogsQuery, ct: ct);
        return rows.Select(row => new FullTextCatalogSnapshot((string)row["CatalogName"]!, (bool)row["IsDefault"]!)).ToList();
    }

    public async Task<List<FullTextIndexSnapshot>> ReadIndexesAsync(ISqlScriptRunner runner, CancellationToken ct)
    {
        var rows = await runner.ExecuteQueryAsync(IndexesQuery, ct: ct);
        var byTable = new Dictionary<ObjectRef, (string CatalogName, string KeyIndexName, string ChangeTracking, List<FullTextIndexColumnSnapshot> Columns)>();

        foreach (var row in rows)
        {
            var tableRef = new ObjectRef((string)row["SchemaName"]!, (string)row["TableName"]!, DatabaseObjectKind.Table);
            if (!byTable.TryGetValue(tableRef, out var entry))
            {
                entry = ((string)row["CatalogName"]!, (string)row["KeyIndexName"]!, (string)row["ChangeTracking"]!, []);
                byTable[tableRef] = entry;
            }

            entry.Columns.Add(new FullTextIndexColumnSnapshot((string)row["ColumnName"]!, Convert.ToInt32(row["LanguageId"])));
        }

        return byTable
            .Select(kv => new FullTextIndexSnapshot(kv.Key, kv.Value.CatalogName, kv.Value.KeyIndexName, kv.Value.ChangeTracking, kv.Value.Columns))
            .ToList();
    }
}
