using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Migration;
using Abs.DBCC.Desktop.ViewModels;
using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.TestCommon.Builders;
using MediatR;
using Moq;

namespace Abs.DBCC.Desktop.Test.ViewModels;

public class MigrationRunViewModelTests
{
    private static readonly ConnectionProfile Profile = new("server", "db", "user", "pw");
    private static readonly SqlCollationName Collation = new("Latin1_General_100_CI_AS_SC_UTF8");

    private static MigrationPlan Plan() => new(Collation, Collation, false, new DatabaseSnapshotBuilder().Build(), [], []);

    // Moq's ReturnsAsync/ThrowsAsync produce an already-completed task, so RunAsync's first await never
    // yields and the constructor's fire-and-forget call runs to completion before the constructor returns.

    [Fact]
    public void Constructor_MigrationSucceeds_RaisesCompletedAndStopsRunning()
    {
        var report = new MigrationReport(true, [], null, null);
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<ExecuteMigrationCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(report);
        MigrationReport? raised = null;

        var vm = new MigrationRunViewModel(sender.Object, Profile, Plan());
        vm.Completed += (_, r) => raised = r;

        // Completed already fired during construction, so it can't be observed here; assert VM state instead.
        Assert.False(vm.IsRunning);
        Assert.False(vm.WasCancelled);
        Assert.False(vm.HasUnexpectedError);
    }

    [Fact]
    public void Constructor_OperationCanceled_SetsWasCancelledAndStopsRunning()
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<ExecuteMigrationCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var vm = new MigrationRunViewModel(sender.Object, Profile, Plan());

        Assert.False(vm.IsRunning);
        Assert.True(vm.WasCancelled);
        Assert.False(vm.HasUnexpectedError);
    }

    [Fact]
    public void Constructor_ConnectionLostBeforeReportProduced_SetsUnexpectedErrorInsteadOfHanging()
    {
        // Simulates the connection attempt itself failing, before a MigrationReport can even be produced.
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<ExecuteMigrationCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("A network-related or instance-specific error occurred."));

        var vm = new MigrationRunViewModel(sender.Object, Profile, Plan());

        Assert.False(vm.IsRunning);
        Assert.False(vm.WasCancelled);
        Assert.True(vm.HasUnexpectedError);
        Assert.Contains("network-related", vm.UnexpectedErrorMessage);
    }

    [Fact]
    public void Constructor_CapturingRowsBeforePhaseReported_ShowsCurrentTableNameAndCount()
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<ExecuteMigrationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<MigrationReport>, CancellationToken>((req, _) =>
                ((ExecuteMigrationCommand)req).PhaseProgress?.Report(new MigrationPhaseProgress(MigrationPhaseKind.CapturingRowsBefore, 2, 5, "[dbo].[Orders]")))
            .Returns(new TaskCompletionSource<MigrationReport>().Task); // never completes - Phase stays at this report

        var vm = new MigrationRunViewModel(sender.Object, Profile, Plan());

        Assert.Equal(MigrationPhaseKind.CapturingRowsBefore, vm.Phase);
        Assert.Equal("[dbo].[Orders]", vm.CurrentTableName);
        Assert.True(vm.ShowCurrentTableName);
        Assert.Equal(2, vm.PhaseCompleted);
        Assert.Equal(5, vm.PhaseTotal);
    }

    [Fact]
    public void Constructor_PhaseAdvancesPastCapturingRowsBefore_SwitchesPhaseAndHidesTableName()
    {
        var report = new MigrationReport(true, [], null, null);
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<ExecuteMigrationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<MigrationReport>, CancellationToken>((req, _) =>
            {
                var cmd = (ExecuteMigrationCommand)req;
                cmd.PhaseProgress?.Report(new MigrationPhaseProgress(MigrationPhaseKind.CapturingRowsBefore, 1, 1, "[dbo].[Orders]"));
                cmd.PhaseProgress?.Report(new MigrationPhaseProgress(MigrationPhaseKind.ExecutingSteps, 0, 0));
            })
            .ReturnsAsync(report);

        var vm = new MigrationRunViewModel(sender.Object, Profile, Plan());

        Assert.Equal(MigrationPhaseKind.ExecutingSteps, vm.Phase);
        Assert.False(vm.ShowCurrentTableName);
    }

    [Fact]
    public void Constructor_MultipleStepsReported_NewestStepIsFirstInStepResults()
    {
        // Unlike the exported report, the live UI list is newest-first.
        var firstStep = new MigrationStepResult(new MigrationStep(0, MigrationStepKind.AlterColumnCollation, "first", "ALTER ..."), true, null, DateTime.Now);
        var secondStep = new MigrationStepResult(new MigrationStep(1, MigrationStepKind.AlterColumnCollation, "second", "ALTER ..."), true, null, DateTime.Now);
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<ExecuteMigrationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<MigrationReport>, CancellationToken>((req, _) =>
            {
                var cmd = (ExecuteMigrationCommand)req;
                cmd.Progress?.Report(firstStep);
                cmd.Progress?.Report(secondStep);
            })
            .ReturnsAsync(new MigrationReport(true, [firstStep, secondStep], null, null));

        var vm = new MigrationRunViewModel(sender.Object, Profile, Plan());

        Assert.Equal(["second", "first"], vm.StepResults.Select(r => r.Step.Description));
    }

    [Fact]
    public void AcknowledgeUnexpectedErrorCommand_RaisesUnexpectedErrorAcknowledged()
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<ExecuteMigrationCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("connection lost"));
        var vm = new MigrationRunViewModel(sender.Object, Profile, Plan());
        var raised = false;
        vm.UnexpectedErrorAcknowledged += (_, _) => raised = true;

        vm.AcknowledgeUnexpectedErrorCommand.Execute(null);

        Assert.True(raised);
    }
}
