using Abs.DBCC.Application.Connections;
using Abs.DBCC.Domain.Migration;

namespace Abs.DBCC.Application.Ports;

public interface IMigrationOrchestrator
{
    /// <summary>
    /// Executes every step of the plan inside a single transaction; rolls back and reports failure
    /// on the first error, leaving the database completely unchanged.
    /// </summary>
    Task<MigrationReport> ExecuteAsync(
        ConnectionProfile profile,
        MigrationPlan plan,
        IProgress<MigrationStepResult>? progress = null,
        CancellationToken ct = default);
}
