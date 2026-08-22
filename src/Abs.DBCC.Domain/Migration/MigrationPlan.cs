using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Domain.Migration;

public sealed record MigrationPlan(
    SqlCollationName SourceCollation,
    SqlCollationName TargetCollation,
    bool UpdateDatabaseDefaultCollation,
    DatabaseSnapshot PreSnapshot,
    IReadOnlyList<MigrationStep> Steps,
    IReadOnlyList<ObjectRef> AffectedTables);
