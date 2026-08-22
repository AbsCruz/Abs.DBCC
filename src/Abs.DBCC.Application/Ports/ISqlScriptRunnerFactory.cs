using Abs.DBCC.Application.Connections;

namespace Abs.DBCC.Application.Ports;

/// <summary>Opens a connection for the given profile and returns a script runner bound to it.</summary>
public interface ISqlScriptRunnerFactory
{
    Task<ISqlScriptRunner> CreateAsync(ConnectionProfile profile, CancellationToken ct = default);
}
