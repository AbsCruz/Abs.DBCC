using Abs.DBCC.Application;
using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Migration;
using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.Infrastructure;
using Abs.DBCC.IntegrationTest.Fixtures;
using Abs.DBCC.IntegrationTest.TestSchema;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Abs.DBCC.IntegrationTest;

/// <summary>
/// End-to-end proof, against a real SQL Server in Docker, that the T-SQL script
/// <see cref="MigrationScriptGenerator"/> produces for a plan is itself valid, runnable T-SQL: executed
/// as a stand-alone script (not via <c>MigrationOrchestrator</c>), it must change the collation exactly
/// like a direct run does, including the trickier database-default-collation path where SQL Server forces
/// the script to be split into three separate transactions/segments around an out-of-transaction
/// <c>ALTER DATABASE ... COLLATE</c> statement.
/// </summary>
public sealed class ScriptExportIntegrationTests(MsSqlContainerFixture fixture) : IClassFixture<MsSqlContainerFixture>
{
    private static readonly SqlCollationName TargetCollation = new("Latin1_General_100_CS_AS");

    [Fact]
    public async Task GeneratedScript_ExecutedStandAlone_AppliesTheSameMigrationAsADirectRun()
    {
        var databaseName = await fixture.CreateTestDatabaseAsync();
        await fixture.ExecuteBatchAsync(databaseName, IntegrationTestSchema.Ddl);
        await fixture.ExecuteBatchAsync(databaseName, IntegrationTestSchema.SeedData);

        var profile = fixture.CreateProfile(databaseName);

        var services = new ServiceCollection().AddApplication().AddInfrastructure();
        await using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        // Under the original case-insensitive source collation, both 'ABC-001' and 'abc-001' match.
        Assert.Equal(2, await CountOrdersWithCustomerCodeAsync(profile, "abc-001"));

        // UpdateDatabaseDefaultCollation: true exercises the harder path - the script must split into
        // three segments around an ALTER DATABASE statement that can't run inside a transaction, same as
        // MigrationOrchestrator does at runtime.
        var plan = await sender.Send(new BuildMigrationPlanCommand(profile, TargetCollation, UpdateDatabaseDefaultCollation: true));

        var script = MigrationScriptGenerator.Generate(plan, databaseName);

        // From here on, no MigrationOrchestrator involved - the script runs the way a user would via
        // sqlcmd/SSMS, through the fixture's plain GO-splitting batch runner.
        await fixture.ExecuteBatchAsync(databaseName, script);

        await AssertColumnsHaveTargetCollationAsync(profile);
        await AssertDatabaseDefaultCollationAsync(profile);

        // After switching to a case-sensitive collation, only the exact-case row matches - proves both
        // the ALTER COLUMN steps and the data itself survived the script run untouched.
        Assert.Equal(1, await CountOrdersWithCustomerCodeAsync(profile, "abc-001"));

        await AssertPermissionsPreservedAsync(profile);
        await AssertIndexedViewStillWorksAsync(profile);
    }

    private static async Task<SqlConnection> OpenConnectionAsync(ConnectionProfile profile, CancellationToken ct = default)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = profile.Server,
            InitialCatalog = profile.Database,
            UserID = profile.User,
            Password = profile.Password,
            Encrypt = profile.Encrypt,
            TrustServerCertificate = profile.TrustServerCertificate,
            Pooling = false
        };

        var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    private static async Task<int> CountOrdersWithCustomerCodeAsync(ConnectionProfile profile, string code)
    {
        await using var connection = await OpenConnectionAsync(profile);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM dbo.Orders WHERE CustomerCode = @code;";
        command.Parameters.AddWithValue("@code", code);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task AssertColumnsHaveTargetCollationAsync(ConnectionProfile profile)
    {
        await using var connection = await OpenConnectionAsync(profile);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.name AS SchemaName, t.name AS TableName, c.name AS ColumnName, c.collation_name AS CollationName
            FROM sys.columns c
            JOIN sys.tables t ON t.object_id = c.object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE c.collation_name IS NOT NULL AND t.is_ms_shipped = 0 AND c.is_computed = 0;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        var checkedColumnCount = 0;
        while (await reader.ReadAsync())
        {
            checkedColumnCount++;
            var schema = reader.GetString(reader.GetOrdinal("SchemaName"));
            var table = reader.GetString(reader.GetOrdinal("TableName"));
            var column = reader.GetString(reader.GetOrdinal("ColumnName"));
            var collation = reader.GetString(reader.GetOrdinal("CollationName"));
            Assert.True(TargetCollation.Value == collation, $"{schema}.{table}.{column} has collation '{collation}', expected '{TargetCollation.Value}'.");
        }

        Assert.True(checkedColumnCount > 0, "expected at least one character column in the test schema");
    }

    private static async Task AssertDatabaseDefaultCollationAsync(ConnectionProfile profile)
    {
        await using var connection = await OpenConnectionAsync(profile);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DATABASEPROPERTYEX(DB_NAME(), 'Collation');";
        var result = (string)(await command.ExecuteScalarAsync())!;
        Assert.Equal(TargetCollation.Value, result);
    }

    private static async Task AssertPermissionsPreservedAsync(ConnectionProfile profile)
    {
        await using var connection = await OpenConnectionAsync(profile);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT dp.permission_name, dp.state_desc
            FROM sys.database_permissions dp
            JOIN sys.database_principals pr ON pr.principal_id = dp.grantee_principal_id
            WHERE pr.name = 'TestPrincipal';
            """;

        await using var reader = await command.ExecuteReaderAsync();
        var permissions = new List<(string Name, string State)>();
        while (await reader.ReadAsync())
            permissions.Add((reader.GetString(0), reader.GetString(1)));

        Assert.Equal(2, permissions.Count(p => p.Name == "SELECT" && p.State == "GRANT"));
        Assert.Contains(permissions, p => p.Name == "UPDATE" && p.State == "GRANT");
        Assert.Contains(permissions, p => p.Name == "DELETE" && p.State == "DENY");
    }

    /// <summary>Proves the script's per-step GO batching recreated the schema-bound indexed view (CREATE VIEW, then its unique clustered index) in working order.</summary>
    private static async Task AssertIndexedViewStillWorksAsync(ConnectionProfile profile)
    {
        await using var connection = await OpenConnectionAsync(profile);

        await using (var indexCommand = connection.CreateCommand())
        {
            indexCommand.CommandText = """
                SELECT i.is_unique, i.type_desc
                FROM sys.indexes i
                JOIN sys.views v ON v.object_id = i.object_id
                WHERE v.name = 'OrdersByCustomerCode' AND i.name = 'IX_OrdersByCustomerCode_Id';
                """;
            await using var reader = await indexCommand.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), "expected the indexed view's unique clustered index to still exist after running the generated script");
            Assert.True(reader.GetBoolean(0));
            Assert.Equal("CLUSTERED", reader.GetString(1));
        }

        await using var dataCommand = connection.CreateCommand();
        dataCommand.CommandText = "SELECT COUNT(*) FROM dbo.OrdersByCustomerCode;";
        var rowCount = (int)(await dataCommand.ExecuteScalarAsync())!;
        Assert.True(rowCount > 0, "expected the indexed view to still return data after running the generated script");
    }
}
