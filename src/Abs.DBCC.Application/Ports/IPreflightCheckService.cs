using Abs.DBCC.Application.Connections;
using Abs.DBCC.Domain.Migration;

namespace Abs.DBCC.Application.Ports;

/// <summary>Cheap checks surfaced to the user before starting a migration: other active sessions and an estimate of how much data is affected.</summary>
public interface IPreflightCheckService
{
    Task<PreflightCheckResult> CheckAsync(ConnectionProfile profile, MigrationPlan plan, CancellationToken ct = default);
}
