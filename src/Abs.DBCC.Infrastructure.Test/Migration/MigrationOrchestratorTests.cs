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
        Assert.Equal(2, report.StepResults.Count);
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
        runner.FailOnNonQueryCallNumber = 2; // the database-collation call
        var plan = Plan(
            new MigrationStep(0, MigrationStepKind.AlterColumnCollation, "alter", "ALTER TABLE ..."),
            new MigrationStep(1, MigrationStepKind.AlterDatabaseCollation, "db collation", "ALTER DATABASE CURRENT COLLATE ..."));

        var report = await sut.ExecuteAsync(Profile, plan);

        Assert.False(report.Succeeded);
        Assert.True(runner.WasCommitted, "column changes must not be rolled back just because the DB-level step failed afterwards");
        Assert.False(runner.WasRolledBack);
        // ALTER DATABASE runs outside a transaction, so an ordinary error there is exactly as uncertain
        // in outcome as a cancellation would be - SQL Server may have already applied the change before
        // reporting the error back, so the report must not categorically claim the step failed.
        Assert.Contains("nicht sicher auszuschließen", report.FailureReason);
        Assert.Contains("prüfen Sie die aktuelle Datenbank-Collation", report.FailureReason);
    }

    [Fact]
    public async Task ExecuteAsync_FailureAndRollbackBothFail_StillReturnsReportInsteadOfThrowing()
    {
        // Simulates a dropped connection: the step fails, then rollback itself fails since there's no
        // connection left to send it over. SQL Server has already rolled back server-side by then, so
        // this must not surface as an unhandled exception - see MigrationOrchestrator.TryRollbackAsync.
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
    public async Task ExecuteAsync_CancelledMidStep_RollsBackAndRethrowsInsteadOfReportingAsFailure()
    {
        // Reproduces a real bug: a cancelled step used to be treated exactly like a SQL error (caught
        // by the broad catch in ExecuteStepAsync), and the rollback that followed reused the same
        // already-cancelled token - which fails before ever sending the ROLLBACK, misreported to the
        // caller as if the connection itself had dropped. Cancellation must instead roll back with a
        // fresh token and propagate as an actual OperationCanceledException.
        var (sut, runner) = CreateSut();
        runner.FailOnNonQueryCallNumber = 2;
        runner.ThrowOnExecute = new OperationCanceledException();
        var plan = Plan(
            new MigrationStep(0, MigrationStepKind.DropIndex, "drop", "DROP INDEX ..."),
            new MigrationStep(1, MigrationStepKind.AlterColumnCollation, "alter", "ALTER TABLE ..."));

        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.ExecuteAsync(Profile, plan));

        Assert.True(runner.WasRolledBack);
        Assert.False(runner.WasCommitted);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledDuringRecreatePhase_ReturnsPartialStateReportInsteadOfBareCancellation()
    {
        // Reproduces a real bug: past this point the database collation is already committed and
        // cannot be rolled back to the original starting point, exactly like an ordinary segment-3
        // failure - a bare OperationCanceledException would surface to the UI as a generic "cancelled"
        // screen implying nothing happened, hiding that manual recovery is actually required.
        var (sut, runner) = CreateSut();
        runner.FailOnNonQueryCallNumber = 3;
        runner.ThrowOnExecute = new OperationCanceledException();
        var plan = Plan(
            new MigrationStep(0, MigrationStepKind.AlterColumnCollation, "alter", "ALTER TABLE ..."),
            new MigrationStep(1, MigrationStepKind.AlterDatabaseCollation, "db collation", "ALTER DATABASE CURRENT COLLATE ..."),
            new MigrationStep(2, MigrationStepKind.CreateIndex, "recreate", "CREATE INDEX ..."));

        var report = await sut.ExecuteAsync(Profile, plan);

        Assert.False(report.Succeeded);
        Assert.Contains("abgebrochen", report.FailureReason);
        Assert.Contains("Manuelles Nacharbeiten erforderlich", report.FailureReason);
        Assert.True(runner.WasRolledBack);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledDuringDatabaseCollationStep_ReturnsPartialStateReportInsteadOfBareCancellation()
    {
        var (sut, runner) = CreateSut();
        runner.FailOnNonQueryCallNumber = 2; // the (only) database-collation call
        runner.ThrowOnExecute = new OperationCanceledException();
        var plan = Plan(
            new MigrationStep(0, MigrationStepKind.AlterColumnCollation, "alter", "ALTER TABLE ..."),
            new MigrationStep(1, MigrationStepKind.AlterDatabaseCollation, "db collation", "ALTER DATABASE CURRENT COLLATE ..."));

        var report = await sut.ExecuteAsync(Profile, plan);

        Assert.False(report.Succeeded);
        Assert.Contains("abgebrochen", report.FailureReason);
        Assert.Contains("ungewiss", report.FailureReason);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledDuringSegment1Commit_ReturnsUncertainStateReport()
    {
        // Reproduces a real gap: unlike a step failing, a cancelled COMMIT is genuinely ambiguous -
        // SQL Server may have already applied it server-side before the client saw the acknowledgment.
        // This must not be allowed to escape as a bare, misleadingly-clean cancellation either.
        var (sut, runner) = CreateSut();
        runner.FailOnCommitCallNumber = 1;
        var plan = Plan(new MigrationStep(0, MigrationStepKind.AlterColumnCollation, "alter", "ALTER TABLE ..."));

        var report = await sut.ExecuteAsync(Profile, plan);

        Assert.False(report.Succeeded);
        Assert.Contains("abgebrochen", report.FailureReason);
        Assert.Contains("ungewiss", report.FailureReason);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledBeforeRecreatePhaseBegins_ReturnsPartialStateReport()
    {
        // The database collation is already committed at this point (we only reach this
        // BeginTransactionAsync call after that step succeeded), but no recreate step has run yet.
        var (sut, runner) = CreateSut();
        runner.FailOnBeginTransactionCallNumber = 2; // the post-collation BeginTransactionAsync
        var plan = Plan(
            new MigrationStep(0, MigrationStepKind.AlterColumnCollation, "alter", "ALTER TABLE ..."),
            new MigrationStep(1, MigrationStepKind.AlterDatabaseCollation, "db collation", "ALTER DATABASE CURRENT COLLATE ..."),
            new MigrationStep(2, MigrationStepKind.CreateIndex, "recreate", "CREATE INDEX ..."));

        var report = await sut.ExecuteAsync(Profile, plan);

        Assert.False(report.Succeeded);
        Assert.Contains("abgebrochen", report.FailureReason);
        Assert.Contains("Manuelles Nacharbeiten erforderlich", report.FailureReason);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledDuringSegment3Commit_ReturnsUncertainStateReport()
    {
        var (sut, runner) = CreateSut();
        runner.FailOnCommitCallNumber = 2; // the final, segment-3 commit
        var plan = Plan(
            new MigrationStep(0, MigrationStepKind.AlterColumnCollation, "alter", "ALTER TABLE ..."),
            new MigrationStep(1, MigrationStepKind.AlterDatabaseCollation, "db collation", "ALTER DATABASE CURRENT COLLATE ..."),
            new MigrationStep(2, MigrationStepKind.CreateIndex, "recreate", "CREATE INDEX ..."));

        var report = await sut.ExecuteAsync(Profile, plan);

        Assert.False(report.Succeeded);
        Assert.Contains("abgebrochen", report.FailureReason);
        Assert.Contains("ungewiss", report.FailureReason);
    }

    [Fact]
    public async Task ExecuteAsync_NonCancellationFailureDuringSegment1Commit_ReturnsUncertainStateReportInsteadOfEscaping()
    {
        // Reproduces a real gap: a timeout or dropped connection during COMMIT is just as ambiguous as
        // a cancellation, but it used to escape uncaught (only OperationCanceledException was handled
        // here) all the way to the generic, context-free unexpected-error screen.
        var (sut, runner) = CreateSut();
        runner.FailOnCommitCallNumber = 1;
        runner.ThrowOnTransactionBoundary = new InvalidOperationException("connection reset");
        var plan = Plan(new MigrationStep(0, MigrationStepKind.AlterColumnCollation, "alter", "ALTER TABLE ..."));

        var report = await sut.ExecuteAsync(Profile, plan);

        Assert.False(report.Succeeded);
        Assert.Contains("connection reset", report.FailureReason);
        Assert.Contains("ungewiss", report.FailureReason);
    }

    [Fact]
    public async Task ExecuteAsync_NonCancellationFailureBeforeRecreatePhaseBegins_ReturnsPartialStateReportInsteadOfEscaping()
    {
        var (sut, runner) = CreateSut();
        runner.FailOnBeginTransactionCallNumber = 2; // the post-collation BeginTransactionAsync
        runner.ThrowOnTransactionBoundary = new InvalidOperationException("connection reset");
        var plan = Plan(
            new MigrationStep(0, MigrationStepKind.AlterColumnCollation, "alter", "ALTER TABLE ..."),
            new MigrationStep(1, MigrationStepKind.AlterDatabaseCollation, "db collation", "ALTER DATABASE CURRENT COLLATE ..."),
            new MigrationStep(2, MigrationStepKind.CreateIndex, "recreate", "CREATE INDEX ..."));

        var report = await sut.ExecuteAsync(Profile, plan);

        Assert.False(report.Succeeded);
        Assert.Contains("connection reset", report.FailureReason);
        Assert.Contains("Manuelles Nacharbeiten erforderlich", report.FailureReason);
    }

    [Fact]
    public async Task ExecuteAsync_NonCancellationFailureDuringSegment3Commit_ReturnsUncertainStateReportInsteadOfEscaping()
    {
        var (sut, runner) = CreateSut();
        runner.FailOnCommitCallNumber = 2; // the final, segment-3 commit
        runner.ThrowOnTransactionBoundary = new InvalidOperationException("connection reset");
        var plan = Plan(
            new MigrationStep(0, MigrationStepKind.AlterColumnCollation, "alter", "ALTER TABLE ..."),
            new MigrationStep(1, MigrationStepKind.AlterDatabaseCollation, "db collation", "ALTER DATABASE CURRENT COLLATE ..."),
            new MigrationStep(2, MigrationStepKind.CreateIndex, "recreate", "CREATE INDEX ..."));

        var report = await sut.ExecuteAsync(Profile, plan);

        Assert.False(report.Succeeded);
        Assert.Contains("connection reset", report.FailureReason);
        Assert.Contains("ungewiss", report.FailureReason);
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
