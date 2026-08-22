using Abs.DBCC.Application.Connections;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Application.Ports;

public interface IDataVerificationService
{
    /// <summary>Reads every row of every table in the snapshot, keyed by table, for later comparison.</summary>
    Task<IReadOnlyList<TableRowsSnapshot>> CaptureRowsAsync(
        ConnectionProfile profile, DatabaseSnapshot snapshot, CancellationToken ct = default);

    /// <summary>Compares two row captures value-by-value and reports any table whose content changed.</summary>
    IReadOnlyList<DataDiff> Compare(IReadOnlyList<TableRowsSnapshot> before, IReadOnlyList<TableRowsSnapshot> after);
}
