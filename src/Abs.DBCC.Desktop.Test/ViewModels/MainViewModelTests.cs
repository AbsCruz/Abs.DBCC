using Abs.DBCC.Application.Collations;
using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Migration;
using Abs.DBCC.Desktop.ViewModels;
using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Inspection;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.SharedKernel;
using Abs.DBCC.TestCommon.Builders;
using MediatR;
using Moq;

namespace Abs.DBCC.Desktop.Test.ViewModels;

public class MainViewModelTests
{
    // updateDatabaseDefaultCollation:true with differing collations keeps IsNoOp false, so BuildPlanCommand raises PlanBuilt instead of bailing out.
    private static MigrationPlan Plan() =>
        new(new("SQL_Latin1_General_CP1_CI_AS"), new("Latin1_General_100_CI_AS_SC_UTF8"),
            true, new DatabaseSnapshotBuilder().Build(), [], []);

    // Every navigation target's constructor kicks off a fire-and-forget load; these tests only care about
    // the connection banner, so unrelated queries are stubbed to a task that never completes.
    private static Mock<ISender> StubSender()
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<TestConnectionQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        sender.Setup(s => s.Send(It.IsAny<GetDatabaseCollationReportQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource<DatabaseCollationReport>().Task);
        sender.Setup(s => s.Send(It.IsAny<GetAvailableCollationsQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource<IReadOnlyList<CollationInfo>>().Task);
        sender.Setup(s => s.Send(It.IsAny<GetPreflightCheckQuery>(), It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource<PreflightCheckResult>().Task);
        sender.Setup(s => s.Send(It.IsAny<ExecuteMigrationCommand>(), It.IsAny<CancellationToken>()))
            .Returns(new TaskCompletionSource<MigrationReport>().Task);
        sender.Setup(s => s.Send(It.IsAny<BuildMigrationPlanCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Plan());
        return sender;
    }

    private static MainViewModel CreateSut(Mock<ISender> sender) => new(
        () => new ConnectionSetupViewModel(sender.Object),
        profile => new CollationOverviewViewModel(sender.Object, profile),
        profile => new TargetCollationPickerViewModel(sender.Object, profile),
        (profile, plan) => new MigrationPlanReviewViewModel(sender.Object, profile, plan),
        (profile, plan, skipDataVerification) => new MigrationRunViewModel(sender.Object, profile, plan, skipDataVerification));

    private static async Task<ConnectionProfile> ConnectAsync(MainViewModel vm)
    {
        var setup = Assert.IsType<ConnectionSetupViewModel>(vm.CurrentViewModel);
        setup.Server = "server1";
        setup.Database = "db1";
        setup.User = "user1";
        setup.Password = "pw1";
        await setup.ContinueCommand.ExecuteAsync(null);
        return new ConnectionProfile(setup.Server, setup.Database, setup.User, setup.Password);
    }

    [Fact]
    public void ConnectionDisplay_Null_BeforeConnecting()
    {
        var vm = CreateSut(StubSender());

        Assert.Null(vm.ConnectionDisplay);
    }

    [Fact]
    public async Task ConnectionDisplay_MentionsServerAndDatabase_AfterConnecting()
    {
        var vm = CreateSut(StubSender());

        await ConnectAsync(vm);

        Assert.IsType<CollationOverviewViewModel>(vm.CurrentViewModel);
        Assert.NotNull(vm.ConnectionDisplay);
        Assert.Contains("server1", vm.ConnectionDisplay);
        Assert.Contains("db1", vm.ConnectionDisplay);
    }

    [Fact]
    public async Task ConnectionDisplay_ClearsAgain_WhenGoingBackToConnectionSetup()
    {
        var vm = CreateSut(StubSender());
        await ConnectAsync(vm);
        var overview = Assert.IsType<CollationOverviewViewModel>(vm.CurrentViewModel);

        overview.BackCommand.Execute(null);

        Assert.IsType<ConnectionSetupViewModel>(vm.CurrentViewModel);
        Assert.Null(vm.ConnectionDisplay);
    }

    [Fact]
    public async Task ConnectionDisplay_StaysSet_AfterBuildingAMigrationPlan()
    {
        var vm = CreateSut(StubSender());
        await ConnectAsync(vm);
        var overview = Assert.IsType<CollationOverviewViewModel>(vm.CurrentViewModel);

        overview.ContinueCommand.Execute(null);
        var picker = Assert.IsType<TargetCollationPickerViewModel>(vm.CurrentViewModel);
        Assert.Contains("db1", vm.ConnectionDisplay);

        picker.SelectedCollation = new CollationInfo("Latin1_General_100_CI_AS_SC_UTF8", "desc");
        await picker.BuildPlanCommand.ExecuteAsync(null);

        Assert.IsType<MigrationPlanReviewViewModel>(vm.CurrentViewModel);
        Assert.Contains("db1", vm.ConnectionDisplay);
    }
}
