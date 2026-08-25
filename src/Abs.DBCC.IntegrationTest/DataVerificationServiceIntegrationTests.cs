using Abs.DBCC.Application;
using Abs.DBCC.Application.Migration;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Infrastructure;
using Abs.DBCC.IntegrationTest.Fixtures;
using Abs.DBCC.IntegrationTest.TestSchema;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Abs.DBCC.IntegrationTest;

/// <summary>
/// Proves, against a real SQL Server, that DataVerificationService's snapshot-isolation path (only
/// taken when the database has ALLOW_SNAPSHOT_ISOLATION enabled) actually works end-to-end - the unit
/// tests against a fake runner only verify the calling contract, not that
/// SqlConnection.BeginTransactionAsync(IsolationLevel.Snapshot, ...) and the
/// sys.databases.snapshot_isolation_state check behave as expected against a real server.
/// </summary>
public sealed class DataVerificationServiceIntegrationTests(MsSqlContainerFixture fixture) : IClassFixture<MsSqlContainerFixture>
{
    [Fact]
    public async Task CaptureRowsAsync_WithSnapshotIsolationEnabled_CapturesConsistentlyAndProducesNoFalseDiffs()
    {
        var databaseName = await fixture.CreateTestDatabaseAsync();
        await fixture.ExecuteBatchAsync(databaseName, IntegrationTestSchema.Ddl);
        await fixture.ExecuteBatchAsync(databaseName, IntegrationTestSchema.SeedData);
        await fixture.ExecuteBatchAsync(databaseName, $"ALTER DATABASE [{databaseName}] SET ALLOW_SNAPSHOT_ISOLATION ON;");

        var profile = fixture.CreateProfile(databaseName);
        var services = new ServiceCollection().AddApplication().AddInfrastructure();
        await using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();
        var dataVerification = provider.GetRequiredService<IDataVerificationService>();

        var plan = await sender.Send(new BuildMigrationPlanCommand(
            profile, new SqlCollationName("Latin1_General_100_CS_AS"), UpdateDatabaseDefaultCollation: false));

        var before = await dataVerification.CaptureRowsAsync(profile, plan.PreSnapshot);
        var after = await dataVerification.CaptureRowsAsync(profile, plan.PreSnapshot);
        var diffs = dataVerification.Compare(before, after);

        Assert.Empty(diffs);
    }
}
