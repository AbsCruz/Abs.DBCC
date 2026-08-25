using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Domain.Migration;

public sealed record MigrationPlan(
    SqlCollationName SourceCollation,
    SqlCollationName TargetCollation,
    bool UpdateDatabaseDefaultCollation,
    DatabaseSnapshot PreSnapshot,
    IReadOnlyList<MigrationStep> Steps,
    IReadOnlyList<ObjectRef> AffectedTables)
{
    /// <summary>
    /// True if the database and every column are already at the target collation, i.e. this plan would
    /// change nothing.
    /// </summary>
    public bool IsNoOp =>
        AffectedTables.Count == 0 && (!UpdateDatabaseDefaultCollation || SourceCollation.Value == TargetCollation.Value);
}
