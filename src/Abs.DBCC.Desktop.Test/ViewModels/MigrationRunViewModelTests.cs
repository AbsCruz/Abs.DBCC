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

    // ExecuteMigrationCommand's ISender.Send call is awaited as the very first statement in RunAsync;
    // Moq's ReturnsAsync/ThrowsAsync produce an already-completed task, so that await never yields, and
    // Start()'s fire-and-forget "_ = RunAsync()" runs to completion synchronously before Start() itself
    // returns. That is exactly why Start() must never run from the constructor (see its doc comment) -
    // these tests assert on the Completed/*Acknowledged events specifically to prove callers really can
    // subscribe first and still observe them, rather than only checking terminal VM state.

    [Fact]
    public void Start_MigrationSucceeds_RaisesCompletedAndStopsRunning()
    {
        var report = new MigrationReport(true, [], null, null);
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<ExecuteMigrationCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(report);
        MigrationReport? raised = null;

        var vm = new MigrationRunViewModel(sender.Object, Profile, Plan());
        vm.Completed += (_, r) => raised = r;
        vm.Start();

        Assert.Same(report, raised);
        Assert.False(vm.IsRunning);
        Assert.False(vm.WasCancelled);
        Assert.False(vm.HasUnexpectedError);
    }

    [Fact]
    public void Start_OperationCanceled_SetsWasCancelledAndStopsRunning()
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<ExecuteMigrationCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var vm = new MigrationRunViewModel(sender.Object, Profile, Plan());
        vm.Start();

        Assert.False(vm.IsRunning);
        Assert.True(vm.WasCancelled);
        Assert.False(vm.HasUnexpectedError);
    }

    [Fact]
    public void Start_ConnectionLostBeforeReportProduced_SetsUnexpectedErrorInsteadOfHanging()
    {
        // Simulates a lost database connection surfacing as a SqlException that escapes
        // ExecuteMigrationCommand entirely (e.g. the very first connection attempt fails), i.e. before
        // MigrationOrchestrator ever gets to produce a MigrationReport.
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<ExecuteMigrationCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("A network-related or instance-specific error occurred."));

        var vm = new MigrationRunViewModel(sender.Object, Profile, Plan());
        vm.Start();

        Assert.False(vm.IsRunning);
        Assert.False(vm.WasCancelled);
        Assert.True(vm.HasUnexpectedError);
        Assert.Contains("network-related", vm.UnexpectedErrorMessage);
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
        vm.Start();

        vm.AcknowledgeUnexpectedErrorCommand.Execute(null);

        Assert.True(raised);
    }

    [Fact]
    public void Constructor_DoesNotStartTheMigration()
    {
        // Reproduces a real bug: MainViewModel wires up Completed/CancelledAcknowledged/
        // UnexpectedErrorAcknowledged only after the view model is constructed. If the constructor
        // itself kicked off the work (and that work happened to complete synchronously - as it does
        // with Moq's ReturnsAsync/ThrowsAsync, and potentially in production for a fast enough
        // operation), the event would already have fired before anyone could subscribe to it.
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<ExecuteMigrationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationReport(true, [], null, null));

        var vm = new MigrationRunViewModel(sender.Object, Profile, Plan());

        Assert.True(vm.IsRunning);
        sender.Verify(s => s.Send(It.IsAny<ExecuteMigrationCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
