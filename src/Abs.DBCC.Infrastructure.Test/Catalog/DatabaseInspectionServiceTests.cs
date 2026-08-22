using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Infrastructure.Catalog;
using Abs.DBCC.TestCommon.Fakes;
using Moq;

namespace Abs.DBCC.Infrastructure.Test.Catalog;

public class DatabaseInspectionServiceTests
{
    private static readonly ConnectionProfile Profile = new("server", "db", "user", "pw");

    private static (DatabaseInspectionService Service, FakeSqlScriptRunner Runner) CreateSut(string databaseCollation)
    {
        var runner = new FakeSqlScriptRunner();
        var runnerFactory = new Mock<ISqlScriptRunnerFactory>();
        runnerFactory.Setup(f => f.CreateAsync(Profile, It.IsAny<CancellationToken>())).ReturnsAsync(runner);

        var catalogService = new Mock<ICollationCatalogService>();
        catalogService
            .Setup(c => c.GetDatabaseDefaultCollationAsync(Profile, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Domain.Collation.SqlCollationName(databaseCollation));

        return (new DatabaseInspectionService(catalogService.Object, runnerFactory.Object), runner);
    }

    private static Dictionary<string, object?> ColumnRow(
        string schema, string table, string column, string dataType, string? collation) =>
        new()
        {
            ["SchemaName"] = schema,
            ["TableName"] = table,
            ["ColumnName"] = column,
            ["SqlDataType"] = dataType,
            ["CollationName"] = collation
        };

    [Fact]
    public async Task BuildCollationReportAsync_GroupsColumnsByTable()
    {
        var (service, runner) = CreateSut("Latin1_General_CI_AS");
        runner.EnqueueQueryResult(
        [
            ColumnRow("dbo", "Orders", "Id", "int", null),
            ColumnRow("dbo", "Orders", "Name", "nvarchar", "Latin1_General_CI_AS"),
            ColumnRow("dbo", "Customers", "Email", "varchar", "Latin1_General_CI_AS")
        ]);

        var report = await service.BuildCollationReportAsync(Profile);

        Assert.Equal("Latin1_General_CI_AS", report.DatabaseDefaultCollation.Value);
        Assert.Equal(2, report.Tables.Count);

        var orders = report.Tables.Single(t => t.TableName == "Orders");
        Assert.Equal(2, orders.Columns.Count);
        Assert.False(orders.Columns.Single(c => c.ColumnName == "Id").IsCharacterType);
        Assert.True(orders.Columns.Single(c => c.ColumnName == "Name").IsCharacterType);
    }

    [Fact]
    public async Task BuildCollationReportAsync_MapsMixedCollationTable()
    {
        var (service, runner) = CreateSut("Latin1_General_CI_AS");
        runner.EnqueueQueryResult(
        [
            ColumnRow("dbo", "Orders", "Name", "nvarchar", "Latin1_General_CI_AS"),
            ColumnRow("dbo", "Orders", "Comment", "nvarchar", "Latin1_General_100_CI_AS_SC_UTF8")
        ]);

        var report = await service.BuildCollationReportAsync(Profile);

        Assert.True(report.Tables.Single().IsMixedCollation);
    }

    [Fact]
    public async Task BuildCollationReportAsync_ReturnsNoTables_WhenNoRows()
    {
        var (service, _) = CreateSut("Latin1_General_CI_AS");

        var report = await service.BuildCollationReportAsync(Profile);

        Assert.Empty(report.Tables);
    }
}
