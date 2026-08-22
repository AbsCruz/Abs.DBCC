using Abs.DBCC.Application.Connections;
using Abs.DBCC.Domain.Collation;

namespace Abs.DBCC.Application.Ports;

public interface ICollationCatalogService
{
    Task<SqlCollationName> GetServerDefaultCollationAsync(ConnectionProfile profile, CancellationToken ct = default);

    Task<SqlCollationName> GetDatabaseDefaultCollationAsync(ConnectionProfile profile, CancellationToken ct = default);

    /// <summary>All collations installed on the SQL Server instance (sys.fn_helpcollations()).</summary>
    Task<IReadOnlyList<CollationInfo>> GetAvailableCollationsAsync(ConnectionProfile profile, CancellationToken ct = default);
}
