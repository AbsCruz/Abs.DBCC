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
            // Pooling off: ALTER DATABASE ... COLLATE invalidates sessions in a way that can leave a pooled
            // connection silently broken (kill state) for the next caller. A fresh connection avoids that.
            Pooling = false
        };

        var connection = new SqlConnection(connectionStringBuilder.ConnectionString);
        await connection.OpenAsync(ct);
        return new SqlScriptRunner(connection);
    }
}
