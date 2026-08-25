using Abs.DBCC.Application.Connections;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Abs.DBCC.IntegrationTest.Fixtures;

/// <summary>Starts one real, ephemeral SQL Server instance in Docker for the whole integration test class.</summary>
public sealed class MsSqlContainerFixture : IAsyncLifetime
{
    private const string SaPassword = "yourStrong(!)Password123";

    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword(SaPassword)
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public ConnectionProfile CreateProfile(string database) => new(
        Server: $"{_container.Hostname},{_container.GetMappedPublicPort(1433)}",
        Database: database,
        User: "sa",
        Password: SaPassword,
        TrustServerCertificate: true,
        Encrypt: true);

    /// <summary>Creates a fresh, uniquely-named test database directly against master.</summary>
    public async Task<string> CreateTestDatabaseAsync(CancellationToken ct = default)
    {
        var databaseName = $"CollationSwitcherTest_{Guid.NewGuid():N}";

        await using var connection = new SqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}] COLLATE SQL_Latin1_General_CP1_CI_AS;";
        await command.ExecuteNonQueryAsync(ct);

        return databaseName;
    }

    /// <summary>Executes a batch of T-SQL statements (split on lines containing only "GO") against the given database.</summary>
    public async Task ExecuteBatchAsync(string database, string script, CancellationToken ct = default)
    {
        var profile = CreateProfile(database);
        var connectionStringBuilder = new SqlConnectionStringBuilder
        {
            DataSource = profile.Server,
            InitialCatalog = profile.Database,
            UserID = profile.User,
            Password = profile.Password,
            Encrypt = profile.Encrypt,
            TrustServerCertificate = profile.TrustServerCertificate,
            // Must be off: a pooled-but-closed connection still counts as a session, blocking the
            // migration's ALTER DATABASE ... COLLATE step, which needs exclusive database access - same
            // reason production disables pooling.
            Pooling = false
        };

        await using var connection = new SqlConnection(connectionStringBuilder.ConnectionString);
        await connection.OpenAsync(ct);

        foreach (var batch in SplitIntoBatches(script))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = batch;
            command.CommandTimeout = 120;
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    private static IEnumerable<string> SplitIntoBatches(string script)
    {
        var lines = script.Split('\n');
        var current = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                if (current.Length > 0 && !string.IsNullOrWhiteSpace(current.ToString()))
                    yield return current.ToString();
                current.Clear();
                continue;
            }

            current.AppendLine(line);
        }

        if (current.Length > 0 && !string.IsNullOrWhiteSpace(current.ToString()))
            yield return current.ToString();
    }
}
