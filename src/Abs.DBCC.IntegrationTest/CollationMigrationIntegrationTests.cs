using Abs.DBCC.Application;
using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Migration;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Infrastructure;
using Abs.DBCC.IntegrationTest.Fixtures;
using Abs.DBCC.IntegrationTest.TestSchema;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Abs.DBCC.IntegrationTest;

/// <summary>
/// End-to-end proof, against a real SQL Server in Docker, that the full migration pipeline (M2-M5)
/// changes the collation as requested while leaving every other structural detail and every row of
/// data exactly as it was before.
/// </summary>
public sealed class CollationMigrationIntegrationTests(MsSqlContainerFixture fixture) : IClassFixture<MsSqlContainerFixture>
{
    private static readonly SqlCollationName TargetCollation = new("Latin1_General_100_CS_AS");

    [Fact]
    public async Task Migration_PreservesStructureAndData_AndActuallyChangesCollationBehavior()
    {
        var databaseName = await fixture.CreateTestDatabaseAsync();
        await fixture.ExecuteBatchAsync(databaseName, IntegrationTestSchema.Ddl);
        await fixture.ExecuteBatchAsync(databaseName, IntegrationTestSchema.SeedData);

        var fullTextAvailable = await TryCreateFullTextSchemaAsync(databaseName);

        var profile = fixture.CreateProfile(databaseName);

        var services = new ServiceCollection().AddApplication().AddInfrastructure();
        await using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        // Under the original case-insensitive source collation, both 'ABC-001' and 'abc-001' match.
        Assert.Equal(2, await CountOrdersWithCustomerCodeAsync(profile, "abc-001"));

        var plan = await sender.Send(new BuildMigrationPlanCommand(profile, TargetCollation, UpdateDatabaseDefaultCollation: true));

        // Exercises PreflightCheckService's raw T-SQL against a real server - its queries are otherwise
        // only unit-tested against a FakeSqlScriptRunner that echoes back whatever rows it's handed, so a
        // reserved-keyword bug in the SQL text itself (e.g. an unquoted "RowCount" alias, which SQL
        // Server rejects because ROWCOUNT is reserved) would not be caught anywhere else.
        var preflight = await sender.Send(new GetPreflightCheckQuery(profile, plan));
        Assert.True(preflight.EstimatedAffectedRowCount >= 0);

        var report = await sender.Send(new ExecuteMigrationCommand(profile, plan));

        Assert.True(report.Succeeded, report.FailureReason ?? "migration reported failure with no reason");
        Assert.NotNull(report.Verification);
        Assert.Empty(report.Verification.DataDiffs);

        // The one expected exception: dbo.OrdersByCustomerCodeRenamed's own captured definition text
        // legitimately differs after the migration. Recreating it corrected its header to embed its
        // current (post-rename) name instead of the stale pre-rename one still baked into the original
        // sys.sql_modules.definition (see RawDefinitionScriptGenerator) - a genuine, desirable text
        // change, not a migration bug, so it is asserted for specifically rather than masked by a
        // blanket-empty check.
        var structuralDiff = Assert.Single(report.Verification.StructuralDiffs);
        Assert.Contains("OrdersByCustomerCodeRenamed", structuralDiff.Details);

        await AssertColumnsHaveTargetCollationAsync(profile);
        await AssertDatabaseDefaultCollationAsync(profile);

        // After switching to a case-sensitive collation, only the exact-case row matches.
        Assert.Equal(1, await CountOrdersWithCustomerCodeAsync(profile, "abc-001"));

        if (fullTextAvailable)
        {
            await WaitForFullTextPopulationAsync(profile);
            await AssertFullTextSearchStillWorksAsync(profile);
        }

        await AssertPermissionsPreservedAsync(profile);
        await AssertIndexedViewStillWorksAsync(profile);
        await AssertDatabaseWideSweepObjectsStillWorkAsync(profile);
        await AssertRenamedViewStillWorksUnderItsCurrentNameAsync(profile);
    }

    /// <summary>Returns false (without failing the test) if the SQL Server image lacks the Full-Text Search component.</summary>
    private async Task<bool> TryCreateFullTextSchemaAsync(string databaseName)
    {
        try
        {
            await fixture.ExecuteBatchAsync(databaseName, IntegrationTestSchema.FullTextDdl);
            return true;
        }
        catch (SqlException ex) when (ex.Message.Contains("Full-Text Search is not installed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
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
            // See MsSqlContainerFixture.ExecuteBatchAsync: a pooled-but-disposed connection still counts
            // as a session for ALTER DATABASE's exclusive-access requirement.
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

    private static async Task WaitForFullTextPopulationAsync(ConnectionProfile profile)
    {
        await using var connection = await OpenConnectionAsync(profile);
        var deadline = DateTime.UtcNow.AddSeconds(60);

        while (DateTime.UtcNow < deadline)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sys.dm_fts_index_population WHERE database_id = DB_ID();";
            var stillPopulating = (int)(await command.ExecuteScalarAsync())! > 0;
            if (!stillPopulating)
                return;

            await Task.Delay(500);
        }
    }

    private static async Task AssertFullTextSearchStillWorksAsync(ConnectionProfile profile)
    {
        await using var connection = await OpenConnectionAsync(profile);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM dbo.Articles WHERE CONTAINS(Title, N'Über') OR CONTAINS(Body, N'Café');";
        var matchCount = (int)(await command.ExecuteScalarAsync())!;
        Assert.True(matchCount > 0, "expected the full-text index to still find matches after the collation change");
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

        // Two distinct SELECT/GRANT rows are expected: one object-level (ON dbo.Orders) and one
        // schema-level (ON SCHEMA::dbo) - proving both survived, not just one of them.
        Assert.Equal(2, permissions.Count(p => p.Name == "SELECT" && p.State == "GRANT"));
        Assert.Contains(permissions, p => p.Name == "UPDATE" && p.State == "GRANT");
        Assert.Contains(permissions, p => p.Name == "DELETE" && p.State == "DENY");
    }

    /// <summary>Proves the indexed view (a schema-bound view with its own unique clustered index) survived structurally intact and functional.</summary>
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
            Assert.True(await reader.ReadAsync(), "expected the indexed view's unique clustered index to still exist after the migration");
            Assert.True(reader.GetBoolean(0));
            Assert.Equal("CLUSTERED", reader.GetString(1));
        }

        await using var dataCommand = connection.CreateCommand();
        dataCommand.CommandText = "SELECT COUNT(*) FROM dbo.OrdersByCustomerCode;";
        var rowCount = (int)(await dataCommand.ExecuteScalarAsync())!;
        Assert.True(rowCount > 0, "expected the indexed view to still return data after the migration");
    }

    /// <summary>
    /// dbo.RegistrationLog has no character column at all, so it is never part of AffectedTables - it
    /// only proves the "updateDatabaseDefaultCollation sweeps the whole database" behavior: its
    /// schema-bound view, its filtered index, its DEFAULT/CHECK constraints (each calling the
    /// schema-bound dbo.GetMinRegistrationDate function) and its IsValidFlag computed column (which
    /// both calls that function AND is selected by the schema-bound view - the three-link chain that
    /// required the combined drop/recreate ordering graph) must all have survived the migration and
    /// still work exactly as before.
    /// </summary>
    private static async Task AssertDatabaseWideSweepObjectsStillWorkAsync(ConnectionProfile profile)
    {
        await using var connection = await OpenConnectionAsync(profile);

        await using (var indexCommand = connection.CreateCommand())
        {
            indexCommand.CommandText = """
                SELECT COUNT(*) FROM sys.indexes i
                JOIN sys.tables t ON t.object_id = i.object_id
                WHERE t.name = 'RegistrationLog' AND i.name = 'IX_RegistrationLog_Id_Filtered';
                """;
            var indexCount = (int)(await indexCommand.ExecuteScalarAsync())!;
            Assert.Equal(1, indexCount);
        }

        await using (var viewCommand = connection.CreateCommand())
        {
            viewCommand.CommandText = "SELECT COUNT(*) FROM dbo.RegistrationLogSummary;";
            var viewRowCount = (int)(await viewCommand.ExecuteScalarAsync())!;
            Assert.True(viewRowCount > 0, "expected the unrelated schema-bound view to still return data after the migration");
        }

        // The DEFAULT constraint must still call the (surviving) function to produce the right value.
        await using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.CommandText = "INSERT INTO dbo.RegistrationLog (Id) VALUES (3);";
            await insertCommand.ExecuteNonQueryAsync();
        }

        await using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.CommandText = "SELECT RegisteredAt, IsValidFlag FROM dbo.RegistrationLog WHERE Id = 3;";
            await using var reader = await selectCommand.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(new DateTime(2000, 1, 1), reader.GetDateTime(0));
            Assert.Equal(1, reader.GetInt32(1));
        }

        // The computed column, selected by the schema-bound view above, must still call the (surviving)
        // function too.
        await using (var summaryCommand = connection.CreateCommand())
        {
            summaryCommand.CommandText = "SELECT IsValidFlag FROM dbo.RegistrationLogSummary WHERE Id = 3;";
            var flag = (int)(await summaryCommand.ExecuteScalarAsync())!;
            Assert.Equal(1, flag);
        }

        // The CHECK constraint must still call the (surviving) function to enforce the rule.
        await using var violatingCommand = connection.CreateCommand();
        violatingCommand.CommandText = "INSERT INTO dbo.RegistrationLog (Id, RegisteredAt) VALUES (4, '1999-01-01');";
        var ex = await Assert.ThrowsAsync<SqlException>(() => violatingCommand.ExecuteNonQueryAsync());
        Assert.Contains("CK_RegistrationLog_RegisteredAt", ex.Message);
    }

    /// <summary>
    /// dbo.OrdersByCustomerCodeRenamed was created under a different name and renamed with sp_rename -
    /// its stored sys.sql_modules.definition still embeds the pre-rename name. Proves
    /// RawDefinitionScriptGenerator rewrote that header on recreate: the view (and its own index, which
    /// can only have been created successfully against the *current* name) must both exist and work
    /// under the renamed identity, with no trace of the old name anywhere.
    /// </summary>
    private static async Task AssertRenamedViewStillWorksUnderItsCurrentNameAsync(ConnectionProfile profile)
    {
        await using var connection = await OpenConnectionAsync(profile);

        await using (var indexCommand = connection.CreateCommand())
        {
            indexCommand.CommandText = """
                SELECT i.is_unique, i.type_desc
                FROM sys.indexes i
                JOIN sys.views v ON v.object_id = i.object_id
                WHERE v.name = 'OrdersByCustomerCodeRenamed' AND i.name = 'IX_OrdersByCustomerCodeRenamed_Id';
                """;
            await using var reader = await indexCommand.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), "expected the renamed indexed view's own index to exist under the current name after the migration");
            Assert.True(reader.GetBoolean(0));
            Assert.Equal("CLUSTERED", reader.GetString(1));
        }

        await using (var dataCommand = connection.CreateCommand())
        {
            dataCommand.CommandText = "SELECT COUNT(*) FROM dbo.OrdersByCustomerCodeRenamed;";
            var rowCount = (int)(await dataCommand.ExecuteScalarAsync())!;
            Assert.True(rowCount > 0, "expected the renamed view to still return data after the migration");
        }

        await using var oldNameCommand = connection.CreateCommand();
        oldNameCommand.CommandText = "SELECT COUNT(*) FROM sys.objects WHERE name = 'OrdersByCustomerCodeOriginal';";
        var oldNameCount = (int)(await oldNameCommand.ExecuteScalarAsync())!;
        Assert.Equal(0, oldNameCount);
    }
}
