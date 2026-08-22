using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Migration;
using Abs.DBCC.Desktop.ViewModels;
using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.TestCommon.Builders;
using MediatR;
using Moq;

namespace Abs.DBCC.Desktop.Test.ViewModels;

public class MigrationPlanReviewViewModelTests
{
    private static readonly ConnectionProfile Profile = new("server", "db", "user", "pw");
    private static readonly SqlCollationName Collation = new("Latin1_General_100_CI_AS_SC_UTF8");

    private static MigrationPlan Plan(params MigrationStep[] steps) =>
        new(Collation, Collation, false, new DatabaseSnapshotBuilder().Build(), steps, []);

    [Fact]
    public void Constructor_GroupsStepsByKind_OrderedByCountDescending()
    {
        var plan = Plan(
            new MigrationStep(0, MigrationStepKind.DropIndex, "a", "sql"),
            new MigrationStep(1, MigrationStepKind.DropIndex, "b", "sql"),
            new MigrationStep(2, MigrationStepKind.AlterColumnCollation, "c", "sql"));
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetPreflightCheckQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreflightCheckResult(0, 0, 0, 0));

        var vm = new MigrationPlanReviewViewModel(sender.Object, Profile, plan);

        Assert.Equal(2, vm.StepCounts.Count);
        Assert.Equal(MigrationStepKind.DropIndex, vm.StepCounts[0].Kind);
        Assert.Equal(2, vm.StepCounts[0].Count);
    }

    [Fact]
    public async Task LoadPreflightAsync_PopulatesPreflightAndLogFileSizeDisplay()
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetPreflightCheckQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreflightCheckResult(1, 100, 10_485_760, 50.0));

        var vm = new MigrationPlanReviewViewModel(sender.Object, Profile, Plan());
        await vm.LoadPreflightCommand.ExecuteAsync(null);

        Assert.NotNull(vm.Preflight);
        Assert.Equal(1, vm.Preflight.OtherActiveSessionCount);
        Assert.Contains("10", vm.LogFileSizeDisplay);
        Assert.Contains("MB", vm.LogFileSizeDisplay);
        Assert.Contains("50", vm.LogFileSizeDisplay);
    }

    [Fact]
    public void LogFileSizeDisplay_WithoutPreflightLoaded_IsNull()
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetPreflightCheckQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource<PreflightCheckResult>().Task); // never completes

        var vm = new MigrationPlanReviewViewModel(sender.Object, Profile, Plan());

        Assert.Null(vm.LogFileSizeDisplay);
    }

    [Fact]
    public void StartCommand_RaisesStartRequestedWithProfileAndPlan()
    {
        var plan = Plan();
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetPreflightCheckQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreflightCheckResult(0, 0, 0, 0));
        var vm = new MigrationPlanReviewViewModel(sender.Object, Profile, plan);
        (ConnectionProfile Profile, MigrationPlan Plan)? raised = null;
        vm.StartRequested += (_, args) => raised = args;

        vm.StartCommand.Execute(null);

        Assert.NotNull(raised);
        Assert.Equal(Profile, raised.Value.Profile);
        Assert.Same(plan, raised.Value.Plan);
    }
}
