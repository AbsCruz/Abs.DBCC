using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Microsoft.Data.SqlClient;

namespace Abs.DBCC.Infrastructure.Sql;

public sealed class SqlScriptRunnerFactory : ISqlScriptRunnerFactory
{
    public async Task<ISqlScriptRunner> CreateAsync(ConnectionProfile profile, CancellationToken ct = default)
    {
        var connectionStringBuilder = new SqlConnectionStringBuilder
        {
            DataSource = profile.Server,
            InitialCatalog = profile.Database,
            UserID = profile.User,
            Password = profile.Password,
            Encrypt = profile.Encrypt,
            TrustServerCertificate = profile.TrustServerCertificate,
            // Pooling is off deliberately: this app opens only a handful of short-lived connections per
            // operation (not a high-throughput service, so pooling buys nothing), and ALTER DATABASE ...
            // COLLATE - part of the migration itself - causes SQL Server to invalidate sessions in a way
            // that can leave a *pooled* connection silently broken for the next caller ("Resetting the
            // connection results in a different state than the initial login... session is in the kill
            // state"). Always establishing a fresh connection avoids that failure mode entirely.
            Pooling = false
        };

        var connection = new SqlConnection(connectionStringBuilder.ConnectionString);
        await connection.OpenAsync(ct);
        return new SqlScriptRunner(connection);
    }
}
