using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Snapshot.Readers;
using Abs.DBCC.TestCommon.Fakes;

namespace Abs.DBCC.Infrastructure.Test.Snapshot.Readers;

public class SchemaBoundDependencyReaderTests
{
    [Fact]
    public async Task ReadAsync_MapsDependencyToReferencedTableAndColumn()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult(
        [
            new Dictionary<string, object?>
            {
                ["DependentSchema"] = "dbo", ["DependentName"] = "OrdersView", ["DependentTypeCode"] = "V",
                ["ReferencedSchema"] = "dbo", ["ReferencedTable"] = "Orders", ["ReferencedColumn"] = "CustomerName"
            }
        ]);

        var result = await new SchemaBoundDependencyReader().ReadAsync(runner, CancellationToken.None);

        var dep = Assert.Single(result);
        Assert.Equal(DatabaseObjectKind.View, dep.DependentObject.Kind);
        Assert.Equal("OrdersView", dep.DependentObject.Name);
        Assert.Equal("Orders", dep.ReferencedTable.Name);
        Assert.Equal("CustomerName", dep.ReferencedColumn);
    }

    [Fact]
    public async Task ReadAsync_MapsFunctionDependent()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult(
        [
            new Dictionary<string, object?>
            {
                ["DependentSchema"] = "dbo", ["DependentName"] = "GetCustomerName", ["DependentTypeCode"] = "FN",
                ["ReferencedSchema"] = "dbo", ["ReferencedTable"] = "Orders", ["ReferencedColumn"] = "CustomerName"
            }
        ]);

        var result = await new SchemaBoundDependencyReader().ReadAsync(runner, CancellationToken.None);

        Assert.Equal(DatabaseObjectKind.Function, result.Single().DependentObject.Kind);
    }
}
