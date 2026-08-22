namespace Abs.DBCC.Domain.Snapshot;

public sealed record FullTextIndexColumnSnapshot(string ColumnName, int LanguageId);

public sealed record FullTextIndexSnapshot(
    ObjectRef Table,
    string CatalogName,
    string KeyIndexName,
    string ChangeTracking,
    IReadOnlyList<FullTextIndexColumnSnapshot> Columns)
{
    public bool CoversColumn(string columnName) =>
        Columns.Any(c => string.Equals(c.ColumnName, columnName, StringComparison.OrdinalIgnoreCase));
}
