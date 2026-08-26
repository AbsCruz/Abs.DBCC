using Abs.DBCC.Application.Collations;
using Abs.DBCC.Application.Connections;
using Abs.DBCC.Desktop.ViewModels;
using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Inspection;
using Abs.DBCC.Domain.Migration;
using MediatR;
using Moq;

namespace Abs.DBCC.Desktop.Test.ViewModels;

public class CollationOverviewViewModelTests
{
    private static readonly ConnectionProfile Profile = new("server", "db", "user", "pw");
    private static readonly SqlCollationName CollationA = new("Latin1_General_CI_AS");
    private static readonly SqlCollationName CollationB = new("SQL_Latin1_General_CP1_CI_AS");

    private static Mock<ISender> CreateSenderReturning(DatabaseCollationReport report)
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetDatabaseCollationReportQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);
        return sender;
    }

    private static ColumnCollationState Column(string table, string name, string type, SqlCollationName? collation) =>
        new("dbo", table, name, type, IsCharacterType: collation is not null, collation);

    [Fact]
    public async Task ShowCollationFilter_False_WhenOnlyOneDistinctCollation()
    {
        var report = new DatabaseCollationReport(CollationA, [
            new TableCollationReport("dbo", "Orders", [Column("Orders", "Name", "varchar", CollationA)])
        ]);
        var vm = new CollationOverviewViewModel(CreateSenderReturning(report).Object, Profile);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.ShowCollationFilter);
    }

    [Fact]
    public async Task ShowCollationFilter_True_WhenMoreThanOneDistinctCollation()
    {
        var report = new DatabaseCollationReport(CollationA, [
            new TableCollationReport("dbo", "Orders", [
                Column("Orders", "Name", "varchar", CollationA),
                Column("Orders", "Notes", "varchar", CollationB)
            ])
        ]);
        var vm = new CollationOverviewViewModel(CreateSenderReturning(report).Object, Profile);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.ShowCollationFilter);
    }

    [Fact]
    public async Task FilteredTables_SelectedFilter_OnlyShowsMatchingColumnsAndDropsEmptyTables()
    {
        var report = new DatabaseCollationReport(CollationA, [
            new TableCollationReport("dbo", "Orders", [
                Column("Orders", "Name", "varchar", CollationA),
                Column("Orders", "Notes", "varchar", CollationB)
            ]),
            new TableCollationReport("dbo", "Products", [
                Column("Products", "Sku", "varchar", CollationA)
            ])
        ]);
        var vm = new CollationOverviewViewModel(CreateSenderReturning(report).Object, Profile);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SelectedFilterOption = vm.FilterOptions.Single(o => Equals(o.Collation, CollationB));

        var table = Assert.Single(vm.FilteredTables);
        Assert.Equal("Orders", table.Row.TableName);
        var column = Assert.Single(table.VisibleColumns);
        Assert.Equal("Notes", column.ColumnName);
    }

    [Fact]
    public async Task FilteredTables_AllOptionSelected_ShowsEveryColumn()
    {
        var report = new DatabaseCollationReport(CollationA, [
            new TableCollationReport("dbo", "Orders", [
                Column("Orders", "Name", "varchar", CollationA),
                Column("Orders", "Notes", "varchar", CollationB)
            ])
        ]);
        var vm = new CollationOverviewViewModel(CreateSenderReturning(report).Object, Profile);
        await vm.LoadCommand.ExecuteAsync(null);

        var table = Assert.Single(vm.FilteredTables);
        Assert.Equal(2, table.VisibleColumns.Count);
    }

    [Fact]
    public async Task ContinueCommand_RaisesContinueRequested_WithColumnsMarkedExcluded()
    {
        var report = new DatabaseCollationReport(CollationA, [
            new TableCollationReport("dbo", "Orders", [
                Column("Orders", "Name", "varchar", CollationA),
                Column("Orders", "Notes", "varchar", CollationA)
            ])
        ]);
        var vm = new CollationOverviewViewModel(CreateSenderReturning(report).Object, Profile);
        await vm.LoadCommand.ExecuteAsync(null);

        var notesColumn = vm.Rows.Single().Columns.Single(c => c.ColumnName == "Notes");
        notesColumn.IsExcluded = true;

        IReadOnlySet<ColumnRef>? raised = null;
        vm.ContinueRequested += (_, excluded) => raised = excluded;

        vm.ContinueCommand.Execute(null);

        Assert.NotNull(raised);
        var excludedColumn = Assert.Single(raised);
        Assert.Equal("Notes", excludedColumn.ColumnName);
    }
}
