namespace Abs.DBCC.Domain.Snapshot;

public sealed record ForeignKeyColumnSnapshot(string ParentColumn, string ReferencedColumn);

public sealed record ForeignKeySnapshot(
    string Name,
    ObjectRef ParentTable,
    ObjectRef ReferencedTable,
    IReadOnlyList<ForeignKeyColumnSnapshot> Columns,
    string DeleteAction,
    string UpdateAction,
    bool IsNotForReplication)
{
    public bool ReferencesParentColumn(string columnName) =>
        Columns.Any(c => string.Equals(c.ParentColumn, columnName, StringComparison.OrdinalIgnoreCase));

    public bool ReferencesReferencedColumn(string columnName) =>
        Columns.Any(c => string.Equals(c.ReferencedColumn, columnName, StringComparison.OrdinalIgnoreCase));
}
