using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Migration;
using Abs.DBCC.TestCommon.Builders;
using Abs.DBCC.TestCommon.Fakes;
using Moq;

namespace Abs.DBCC.Infrastructure.Test.Migration;

public class PreflightCheckServiceTests
{
    private static readonly ConnectionProfile Profile = new("server", "db", "user", "pw");
    private static readonly SqlCollationName Target = new("Latin1_General_100_CI_AS_SC_UTF8");

    private static (PreflightCheckService Service, FakeSqlScriptRunner Runner) CreateSut()
    {
        var runner = new FakeSqlScriptRunner();
        var factory = new Mock<ISqlScriptRunnerFactory>();
        factory.Setup(f => f.CreateAsync(Profile, It.IsAny<CancellationToken>())).ReturnsAsync(runner);
        return (new PreflightCheckService(factory.Object), runner);
    }

    private static Dictionary<string, object?> LogSpaceRow(long totalBytes, double usedPercent) =>
        new() { ["total_log_size_in_bytes"] = totalBytes, ["used_log_space_in_percent"] = usedPercent };

    [Fact]
    public async Task CheckAsync_SumsRowCountsOnlyForAffectedTables()
    {
        var (sut, runner) = CreateSut();
        runner.EnqueueScalarResult(2); // active sessions
        runner.EnqueueQueryResult(
        [
            new Dictionary<string, object?> { ["SchemaName"] = "dbo", ["TableName"] = "Orders", ["RowCount"] = 100L },
            new Dictionary<string, object?> { ["SchemaName"] = "dbo", ["TableName"] = "Logs", ["RowCount"] = 99999L }
        ]);
        runner.EnqueueQueryResult([LogSpaceRow(1_000_000, 12.5)]);

        var affectedTable = new ObjectRef("dbo", "Orders", DatabaseObjectKind.Table);
        var plan = new Domain.Migration.MigrationPlan(Target, Target, false, new DatabaseSnapshotBuilder().Build(), [], [affectedTable]);

        var result = await sut.CheckAsync(Profile, plan);

        Assert.Equal(2, result.OtherActiveSessionCount);
        Assert.Equal(100, result.EstimatedAffectedRowCount);
    }

    [Fact]
    public async Task CheckAsync_NoAffectedTables_ReturnsZeroRowCount()
    {
        var (sut, runner) = CreateSut();
        runner.EnqueueScalarResult(0);
        runner.EnqueueQueryResult(
        [
            new Dictionary<string, object?> { ["SchemaName"] = "dbo", ["TableName"] = "Orders", ["RowCount"] = 100L }
        ]);
        runner.EnqueueQueryResult([LogSpaceRow(1_000_000, 12.5)]);

        var plan = new Domain.Migration.MigrationPlan(Target, Target, false, new DatabaseSnapshotBuilder().Build(), [], []);

        var result = await sut.CheckAsync(Profile, plan);

        Assert.Equal(0, result.EstimatedAffectedRowCount);
    }

    [Fact]
    public async Task CheckAsync_MapsLogSpaceUsage()
    {
        var (sut, runner) = CreateSut();
        runner.EnqueueScalarResult(0);
        runner.EnqueueQueryResult([]);
        runner.EnqueueQueryResult([LogSpaceRow(536_870_912, 47.25)]);

        var plan = new Domain.Migration.MigrationPlan(Target, Target, false, new DatabaseSnapshotBuilder().Build(), [], []);

        var result = await sut.CheckAsync(Profile, plan);

        Assert.Equal(536_870_912, result.LogFileSizeBytes);
        Assert.Equal(47.25, result.LogUsedPercent);
    }
}
