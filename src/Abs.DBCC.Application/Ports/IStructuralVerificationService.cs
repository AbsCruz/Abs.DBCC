using Abs.DBCC.Application.Connections;
using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Application.Ports;

public interface IStructuralVerificationService
{
    /// <summary>
    /// Re-captures the current schema and diffs it against <paramref name="before"/>, ignoring the
    /// expected collation change on columns that were migrated to <paramref name="targetCollation"/>.
    /// </summary>
    Task<IReadOnlyList<StructuralDiff>> VerifyAsync(
        ConnectionProfile profile,
        DatabaseSnapshot before,
        SqlCollationName targetCollation,
        CancellationToken ct = default);
}
