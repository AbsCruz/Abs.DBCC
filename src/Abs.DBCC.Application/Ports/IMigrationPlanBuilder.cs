using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Application.Ports;

/// <summary>
/// Pure planning logic: given a captured snapshot and a target collation, decides which objects
/// must be dropped, altered and recreated, and in what order. No I/O — fully unit-testable in memory.
/// </summary>
public interface IMigrationPlanBuilder
{
    MigrationPlan Build(DatabaseSnapshot snapshot, SqlCollationName targetCollation, bool updateDatabaseDefaultCollation, IReadOnlySet<ColumnRef>? excludedColumns = null);
}
