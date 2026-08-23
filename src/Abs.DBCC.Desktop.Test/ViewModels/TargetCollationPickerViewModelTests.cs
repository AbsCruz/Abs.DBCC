using Abs.DBCC.Application.Collations;
using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Migration;
using Abs.DBCC.Desktop.ViewModels;
using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.TestCommon.Builders;
using MediatR;
using Moq;

namespace Abs.DBCC.Desktop.Test.ViewModels;

public class TargetCollationPickerViewModelTests
{
    private static readonly ConnectionProfile Profile = new("server", "db", "user", "pw");

    private static readonly IReadOnlyList<CollationInfo> Collations =
    [
        new CollationInfo("Latin1_General_CI_AS", "Case-insensitive, accent-sensitive"),
        new CollationInfo("Latin1_General_CS_AS", "Case-sensitive, accent-sensitive"),
        new CollationInfo("SQL_Latin1_General_CP1_CI_AS", "SQL Server default")
    ];

    private static Mock<ISender> CreateSenderReturningCollations()
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetAvailableCollationsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Collations);
        return sender;
    }

    [Fact]
    public async Task LoadAsync_PopulatesCollations()
    {
        var sender = CreateSenderReturningCollations();
        var vm = new TargetCollationPickerViewModel(sender.Object, Profile);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.Collations.Count);
    }

    [Fact]
    public async Task FilteredCollations_WithSearchText_FiltersCaseInsensitively()
    {
        var sender = CreateSenderReturningCollations();
        var vm = new TargetCollationPickerViewModel(sender.Object, Profile);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SearchText = "cs_as";

        Assert.Single(vm.FilteredCollations);
        Assert.Equal("Latin1_General_CS_AS", vm.FilteredCollations.Single().Name);
    }

    [Fact]
    public async Task FilteredCollations_BlankSearchText_ReturnsAllCollations()
    {
        var sender = CreateSenderReturningCollations();
        var vm = new TargetCollationPickerViewModel(sender.Object, Profile);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SearchText = "";

        Assert.Equal(3, vm.FilteredCollations.Count());
    }

    [Fact]
    public void BuildPlanCommand_CannotExecute_WhenNoCollationSelected()
    {
        var sender = CreateSenderReturningCollations();
        var vm = new TargetCollationPickerViewModel(sender.Object, Profile);

        Assert.False(vm.BuildPlanCommand.CanExecute(null));
    }

    [Fact]
    public async Task BuildPlanCommand_SelectedCollation_RaisesPlanBuilt()
    {
        var source = new SqlCollationName("SQL_Latin1_General_CP1_CI_AS");
        var target = new SqlCollationName("Latin1_General_CS_AS");
        var affectedTable = new ObjectRef("dbo", "Orders", DatabaseObjectKind.Table);
        var plan = new MigrationPlan(source, target, true, new DatabaseSnapshotBuilder().Build(), [], [affectedTable]);

        var sender = CreateSenderReturningCollations();
        sender.Setup(s => s.Send(It.IsAny<BuildMigrationPlanCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);

        var vm = new TargetCollationPickerViewModel(sender.Object, Profile)
        {
            SelectedCollation = new CollationInfo("Latin1_General_CS_AS", "Case-sensitive, accent-sensitive")
        };

        (ConnectionProfile Profile, MigrationPlan Plan)? raised = null;
        vm.PlanBuilt += (_, args) => raised = args;

        Assert.True(vm.BuildPlanCommand.CanExecute(null));
        await vm.BuildPlanCommand.ExecuteAsync(null);

        Assert.NotNull(raised);
        Assert.Same(plan, raised.Value.Plan);
        Assert.Null(vm.NoticeMessage);
    }

    [Fact]
    public async Task BuildPlanCommand_NoOpPlan_SetsNoticeMessageInsteadOfRaisingPlanBuilt()
    {
        var target = new SqlCollationName("Latin1_General_CS_AS");
        var plan = new MigrationPlan(target, target, true, new DatabaseSnapshotBuilder().Build(), [], []);

        var sender = CreateSenderReturningCollations();
        sender.Setup(s => s.Send(It.IsAny<BuildMigrationPlanCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);

        var vm = new TargetCollationPickerViewModel(sender.Object, Profile)
        {
            SelectedCollation = new CollationInfo("Latin1_General_CS_AS", "Case-sensitive, accent-sensitive")
        };

        var raised = false;
        vm.PlanBuilt += (_, _) => raised = true;

        await vm.BuildPlanCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.NotNull(vm.NoticeMessage);
        Assert.Contains("Latin1_General_CS_AS", vm.NoticeMessage);
    }
}
