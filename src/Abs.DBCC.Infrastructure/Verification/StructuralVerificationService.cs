using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Verification;

public sealed class StructuralVerificationService(ISchemaSnapshotService snapshotService) : IStructuralVerificationService
{
    public async Task<IReadOnlyList<StructuralDiff>> VerifyAsync(
        ConnectionProfile profile, DatabaseSnapshot before, SqlCollationName targetCollation, CancellationToken ct = default)
    {
        var after = await snapshotService.CaptureAsync(profile, ct);
        return DatabaseSnapshotComparer.Compare(before, after, targetCollation);
    }
}
