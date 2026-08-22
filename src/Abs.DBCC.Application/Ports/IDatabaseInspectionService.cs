using Abs.DBCC.Application.Connections;
using Abs.DBCC.Domain.Inspection;

namespace Abs.DBCC.Application.Ports;

public interface IDatabaseInspectionService
{
    /// <summary>Builds the per-table/per-column collation report for every user table.</summary>
    Task<DatabaseCollationReport> BuildCollationReportAsync(ConnectionProfile profile, CancellationToken ct = default);
}
