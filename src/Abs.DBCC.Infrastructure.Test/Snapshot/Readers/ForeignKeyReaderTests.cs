using Abs.DBCC.Infrastructure.Snapshot.Readers;
using Abs.DBCC.TestCommon.Fakes;

namespace Abs.DBCC.Infrastructure.Test.Snapshot.Readers;

public class ForeignKeyReaderTests
{
    [Fact]
    public async Task ReadAsync_GroupsMultiColumnForeignKeyIntoOneSnapshot()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult(
        [
            new Dictionary<string, object?>
            {
                ["ForeignKeyName"] = "FK_OrderItems_Orders", ["ParentSchema"] = "dbo", ["ParentTable"] = "OrderItems",
                ["ReferencedSchema"] = "dbo", ["ReferencedTable"] = "Orders",
                ["DeleteAction"] = "CASCADE", ["UpdateAction"] = "NO_ACTION", ["IsNotForReplication"] = false,
                ["ParentColumn"] = "OrderTenant", ["ReferencedColumn"] = "Tenant", ["ColumnOrdinal"] = 1
            },
            new Dictionary<string, object?>
            {
                ["ForeignKeyName"] = "FK_OrderItems_Orders", ["ParentSchema"] = "dbo", ["ParentTable"] = "OrderItems",
                ["ReferencedSchema"] = "dbo", ["ReferencedTable"] = "Orders",
                ["DeleteAction"] = "CASCADE", ["UpdateAction"] = "NO_ACTION", ["IsNotForReplication"] = false,
                ["ParentColumn"] = "OrderId", ["ReferencedColumn"] = "Id", ["ColumnOrdinal"] = 2
            }
        ]);

        var result = await new ForeignKeyReader().ReadAsync(runner, CancellationToken.None);

        var fk = Assert.Single(result);
        Assert.Equal(2, fk.Columns.Count);
        Assert.True(fk.ReferencesParentColumn("OrderId"));
        Assert.True(fk.ReferencesReferencedColumn("Tenant"));
        Assert.Equal("Orders", fk.ReferencedTable.Name);
    }
}
