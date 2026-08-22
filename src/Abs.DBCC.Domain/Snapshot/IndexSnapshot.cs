namespace Abs.DBCC.Domain.Snapshot;

public sealed record IndexColumnSnapshot(string ColumnName, bool IsDescending, bool IsIncluded);

public sealed record IndexSnapshot(
    string Name,
    bool IsUnique,
    bool IsClustered,
    bool IsPrimaryKey,
    bool IsUniqueConstraint,
    IReadOnlyList<IndexColumnSnapshot> Columns,
    string? FilterDefinition)
{
    /// <summary>True if the constraint/index is itself defined via ALTER TABLE ... ADD CONSTRAINT (PK/unique), false for a plain CREATE INDEX.</summary>
    public bool IsTableConstraint => IsPrimaryKey || IsUniqueConstraint;

    public bool CoversColumn(string columnName) =>
        Columns.Any(c => string.Equals(c.ColumnName, columnName, StringComparison.OrdinalIgnoreCase));
}
