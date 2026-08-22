namespace Abs.DBCC.Domain.Snapshot;

public sealed record TableSnapshot(
    ObjectRef Ref,
    IReadOnlyList<ColumnSnapshot> Columns,
    IReadOnlyList<IndexSnapshot> Indexes,
    IReadOnlyList<CheckConstraintSnapshot> CheckConstraints,
    IReadOnlyList<DefaultConstraintSnapshot> DefaultConstraints)
{
    /// <summary>Character columns whose collation differs from the given target.</summary>
    public IEnumerable<ColumnSnapshot> ColumnsRequiringCollationChange(Domain.Collation.SqlCollationName target) =>
        Columns.Where(c => c.IsCharacterType && c.Collation is not null && c.Collation != target);
}
