using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Migration;
using Abs.DBCC.TestCommon.Builders;
using Abs.DBCC.TestCommon.Fakes;
using Moq;

namespace Abs.DBCC.Infrastructure.Test.Migration;

public class MigrationOrchestratorTests
{
    private static readonly ConnectionProfile Profile = new("server", "db", "user", "pw");
    private static readonly SqlCollationName Target = new("Latin1_General_100_CI_AS_SC_UTF8");

    private static (MigrationOrchestrator Orchestrator, FakeSqlScriptRunner Runner) CreateSut()
    {
        var runner = new FakeSqlScriptRunner();
        var factory = new Mock<ISqlScriptRunnerFactory>();
        factory.Setup(f => f.CreateAsync(Profile, It.IsAny<CancellationToken>())).ReturnsAsync(runner);

        return (new MigrationOrchestrator(factory.Object), runner);
    }

    private static Domain.Migration.MigrationPlan Plan(params MigrationStep[] steps) =>
        new(Target, Target, false, new DatabaseSnapshotBuilder().Build(), steps, []);

    [Fact]
    public async Task ExecuteAsync_AllStepsSucceed_CommitsAndReportsSuccess()
    {
        var (sut, runner) = CreateSut();
        var plan = Plan(
            new MigrationStep(0, MigrationStepKind.DropIndex, "drop", "DROP INDEX ..."),
            new MigrationStep(1, MigrationStepKind.AlterColumnCollation, "alter", "ALTER TABLE ..."));

        var report = await sut.ExecuteAsync(Profile, plan);

        Assert.True(report.Succeeded);
        Assert.Equal(2, report.StepResults.Count);
        Assert.All(report.StepResults, r => Assert.True(r.Succeeded));
        Assert.True(runner.WasCommitted);
        Assert.False(runner.WasRolledBack);
        Assert.Equal(["DROP INDEX ...", "ALTER TABLE ..."], runner.ExecutedSql);
    }

    [Fact]
    public async Task ExecuteAsync_FailureMidPlan_RollsBackAndStopsExecutingRemainingSteps()
    {
        var (sut, runner) = CreateSut();
        runner.FailOnNonQueryCallNumber = 2;
        var plan = Plan(
            new MigrationStep(0, MigrationStepKind.DropIndex, "drop", "DROP INDEX ..."),
            new MigrationStep(1, MigrationStepKind.AlterColumnCollation, "alter", "ALTER TABLE ..."),
            new MigrationStep(2, MigrationStepKind.CreateIndex, "recreate", "CREATE INDEX ..."));

        var report = await sut.ExecuteAsync(Profile, plan);

        Assert.False(report.Succeeded);
        Assert.NotNull(report.FailureReason);
        Assert.Equal(2, report.StepResults.Count); // the third step never ran
        Assert.False(report.StepResults[1].Succeeded);
        Assert.True(runner.WasRolledBack);
        Assert.False(runner.WasCommitted);
    }

    [Fact]
    public async Task ExecuteAsync_DatabaseCollationStep_RunsAfterTransactionCommits()
    {
        var (sut, runner) = CreateSut();
        var plan = Plan(
            new MigrationStep(0, MigrationStepKind.AlterColumnCollation, "alter", "ALTER TABLE ..."),
            new MigrationStep(1, MigrationStepKind.AlterDatabaseCollation, "db collation", "ALTER DATABASE CURRENT COLLATE ..."));

        var report = await sut.ExecuteAsync(Profile, plan);

        Assert.True(report.Succeeded);
        Assert.True(runner.WasCommitted);
        Assert.Equal(["ALTER TABLE ...", "ALTER DATABASE CURRENT COLLATE ..."], runner.ExecutedSql);
    }

    [Fact]
    public async Task ExecuteAsync_DatabaseCollationStepFails_TransactionalPartStaysCommitted()
    {
        var (sut, runner) = CreateSut();
        runner.FailOnNonQueryCallNumber = 2; // the (only) database-collation call
        var plan = Plan(
            new MigrationStep(0, MigrationStepKind.AlterColumnCollation, "alter", "ALTER TABLE ..."),
            new MigrationStep(1, MigrationStepKind.AlterDatabaseCollation, "db collation", "ALTER DATABASE CURRENT COLLATE ..."));

        var report = await sut.ExecuteAsync(Profile, plan);

        Assert.False(report.Succeeded);
        Assert.True(runner.WasCommitted, "column changes must not be rolled back just because the DB-level step failed afterwards");
        Assert.False(runner.WasRolledBack);
    }

    [Fact]
    public async Task ExecuteAsync_FailureAndRollbackBothFail_StillReturnsReportInsteadOfThrowing()
    {
        // Simulates the connection dropping mid-step: the step itself fails, and the subsequent
        // rollback attempt also fails because there is no live connection left to send it over.
        // SQL Server has already rolled back server-side in that case, so this must not surface as
        // an unhandled exception - see MigrationOrchestrator.TryRollbackAsync.
        var (sut, runner) = CreateSut();
        runner.FailOnNonQueryCallNumber = 2;
        runner.ThrowOnRollback = new InvalidOperationException("connection closed");
        var plan = Plan(
            new MigrationStep(0, MigrationStepKind.DropIndex, "drop", "DROP INDEX ..."),
            new MigrationStep(1, MigrationStepKind.AlterColumnCollation, "alter", "ALTER TABLE ..."));

        var report = await sut.ExecuteAsync(Profile, plan);

        Assert.False(report.Succeeded);
        Assert.Contains("Schritt 'alter' fehlgeschlagen", report.FailureReason);
        Assert.Contains("connection closed", report.FailureReason);
        Assert.False(runner.WasRolledBack);
        Assert.False(runner.WasCommitted);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsProgressPerStep()
    {
        var (sut, _) = CreateSut();
        var plan = Plan(new MigrationStep(0, MigrationStepKind.AlterColumnCollation, "alter", "ALTER TABLE ..."));
        var progress = new RecordingProgress();

        await sut.ExecuteAsync(Profile, plan, progress);

        Assert.Single(progress.Reports);
    }

    private sealed class RecordingProgress : IProgress<MigrationStepResult>
    {
        public List<MigrationStepResult> Reports { get; } = [];
        public void Report(MigrationStepResult value) => Reports.Add(value);
    }
}
