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
    /// True if the database and every table's columns are already in the target collation, so this plan
    /// has no steps that would actually change anything (a database-collation-only plan built while
    /// <see cref="UpdateDatabaseDefaultCollation"/> is requested but the database is already at the
    /// target still counts as a no-op).
    /// </summary>
    public bool IsNoOp =>
        AffectedTables.Count == 0 && (!UpdateDatabaseDefaultCollation || SourceCollation.Value == TargetCollation.Value);
}
