using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Migration;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.TestCommon.Builders;
using Moq;

namespace Abs.DBCC.Application.Test.Migration;

public class ExecuteMigrationCommandHandlerTests
{
    private static readonly ConnectionProfile Profile = new("server", "db", "user", "pw");
    private static readonly SqlCollationName Target = new("Latin1_General_100_CI_AS_SC_UTF8");
    private static readonly DatabaseSnapshot Snapshot = new DatabaseSnapshotBuilder().Build();
    private static readonly MigrationPlan Plan = new(Snapshot.DatabaseCollation, Target, false, Snapshot, [], []);

    [Fact]
    public async Task Handle_SuccessfulMigration_AttachesVerificationResult()
    {
        var orchestrator = new Mock<IMigrationOrchestrator>();
        orchestrator
            .Setup(o => o.ExecuteAsync(Profile, Plan, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationReport(true, [], null, null));

        var structural = new Mock<IStructuralVerificationService>();
        structural
            .Setup(s => s.VerifyAsync(Profile, Snapshot, Target, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<StructuralDiff>)[]);

        var dataVerification = new Mock<IDataVerificationService>();
        dataVerification
            .Setup(d => d.CaptureRowsAsync(Profile, Snapshot, It.IsAny<IProgress<TableCaptureProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TableRowsSnapshot>)[]);
        dataVerification.Setup(d => d.Compare(It.IsAny<IReadOnlyList<TableRowsSnapshot>>(), It.IsAny<IReadOnlyList<TableRowsSnapshot>>()))
            .Returns((IReadOnlyList<DataDiff>)[]);

        var handler = new ExecuteMigrationCommandHandler(orchestrator.Object, structural.Object, dataVerification.Object);

        var result = await handler.Handle(new ExecuteMigrationCommand(Profile, Plan), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Verification);
        Assert.True(result.Verification.IsSuccess);
        dataVerification.Verify(d => d.CaptureRowsAsync(Profile, Snapshot, It.IsAny<IProgress<TableCaptureProgress>?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_FailedMigration_SkipsVerificationAndReturnsOriginalReport()
    {
        var failedReport = new MigrationReport(false, [], "boom", null);
        var orchestrator = new Mock<IMigrationOrchestrator>();
        orchestrator
            .Setup(o => o.ExecuteAsync(Profile, Plan, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedReport);

        var structural = new Mock<IStructuralVerificationService>();
        var dataVerification = new Mock<IDataVerificationService>();
        dataVerification
            .Setup(d => d.CaptureRowsAsync(Profile, Snapshot, It.IsAny<IProgress<TableCaptureProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TableRowsSnapshot>)[]);

        var handler = new ExecuteMigrationCommandHandler(orchestrator.Object, structural.Object, dataVerification.Object);

        var result = await handler.Handle(new ExecuteMigrationCommand(Profile, Plan), CancellationToken.None);

        Assert.Same(failedReport, result);
        structural.Verify(s => s.VerifyAsync(It.IsAny<ConnectionProfile>(), It.IsAny<DatabaseSnapshot>(), It.IsAny<SqlCollationName>(), It.IsAny<CancellationToken>()), Times.Never);
        dataVerification.Verify(d => d.CaptureRowsAsync(Profile, Snapshot, It.IsAny<IProgress<TableCaptureProgress>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SkipDataVerificationRequested_SkipsCaptureAndCompareButStillRunsStructuralCheck()
    {
        var orchestrator = new Mock<IMigrationOrchestrator>();
        orchestrator
            .Setup(o => o.ExecuteAsync(Profile, Plan, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationReport(true, [], null, null));

        var structural = new Mock<IStructuralVerificationService>();
        structural
            .Setup(s => s.VerifyAsync(Profile, Snapshot, Target, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<StructuralDiff>)[]);

        var dataVerification = new Mock<IDataVerificationService>();

        var handler = new ExecuteMigrationCommandHandler(orchestrator.Object, structural.Object, dataVerification.Object);

        var result = await handler.Handle(new ExecuteMigrationCommand(Profile, Plan, SkipDataVerification: true), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Verification);
        Assert.True(result.Verification.DataVerificationSkipped);
        Assert.Empty(result.Verification.DataDiffs);
        structural.Verify(s => s.VerifyAsync(Profile, Snapshot, Target, It.IsAny<CancellationToken>()), Times.Once);
        dataVerification.Verify(
            d => d.CaptureRowsAsync(It.IsAny<ConnectionProfile>(), It.IsAny<DatabaseSnapshot>(), It.IsAny<IProgress<TableCaptureProgress>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        dataVerification.Verify(
            d => d.Compare(It.IsAny<IReadOnlyList<TableRowsSnapshot>>(), It.IsAny<IReadOnlyList<TableRowsSnapshot>>()),
            Times.Never);
    }
}
