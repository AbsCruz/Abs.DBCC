using Abs.DBCC.Application.Connections;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Application.Ports;

public interface ISchemaSnapshotService
{
    /// <summary>Captures the full structural snapshot (tables, columns, constraints, indexes, foreign keys) of the database.</summary>
    Task<DatabaseSnapshot> CaptureAsync(ConnectionProfile profile, CancellationToken ct = default);
}
