using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Snapshot.Readers;
using Abs.DBCC.TestCommon.Fakes;

namespace Abs.DBCC.Infrastructure.Test.Snapshot.Readers;

public class IndexReaderTests
{
    private static Dictionary<string, object?> Row(
        string indexName, bool isUnique, string typeDesc, bool isPk, bool isUq, string? filter,
        string columnName, bool isDescending, bool isIncluded, string parentName = "Orders", string parentTypeCode = "U") =>
        new()
        {
            ["SchemaName"] = "dbo",
            ["ParentName"] = parentName,
            ["ParentTypeCode"] = parentTypeCode,
            ["IndexName"] = indexName,
            ["IsUnique"] = isUnique,
            ["TypeDesc"] = typeDesc,
            ["IsPrimaryKey"] = isPk,
            ["IsUniqueConstraint"] = isUq,
            ["FilterDefinition"] = filter,
            ["IsDescendingKey"] = isDescending,
            ["IsIncludedColumn"] = isIncluded,
            ["KeyOrdinal"] = (byte)(isIncluded ? 0 : 1),
            ["IndexColumnId"] = (short)1,
            ["ColumnName"] = columnName
        };

    [Fact]
    public async Task ReadAsync_GroupsMultiColumnIndexIntoOneSnapshot()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult(
        [
            Row("IX_Orders_Name_Total", false, "NONCLUSTERED", false, false, null, "Name", false, false),
            Row("IX_Orders_Name_Total", false, "NONCLUSTERED", false, false, null, "Total", true, false)
        ]);

        var (tableIndexes, viewIndexes) = await new IndexReader().ReadAsync(runner, CancellationToken.None);

        var indexes = tableIndexes[new ObjectRef("dbo", "Orders", DatabaseObjectKind.Table)];
        var index = Assert.Single(indexes);
        Assert.Equal(2, index.Columns.Count);
        Assert.False(index.IsClustered);
        Assert.False(index.CoversColumn("DoesNotExist"));
        Assert.True(index.CoversColumn("total"));
        Assert.Empty(viewIndexes);
    }

    [Fact]
    public async Task ReadAsync_MapsPrimaryKeyFlags()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult(
        [
            Row("PK_Orders", true, "CLUSTERED", true, false, null, "Id", false, false)
        ]);

        var (tableIndexes, _) = await new IndexReader().ReadAsync(runner, CancellationToken.None);

        var index = tableIndexes[new ObjectRef("dbo", "Orders", DatabaseObjectKind.Table)][0];
        Assert.True(index.IsPrimaryKey);
        Assert.True(index.IsClustered);
        Assert.True(index.IsTableConstraint);
    }

    [Fact]
    public async Task ReadAsync_MapsIncludedColumns()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult(
        [
            Row("IX_Orders_Name", false, "NONCLUSTERED", false, false, null, "Name", false, false),
            Row("IX_Orders_Name", false, "NONCLUSTERED", false, false, null, "Comment", false, true)
        ]);

        var (tableIndexes, _) = await new IndexReader().ReadAsync(runner, CancellationToken.None);

        var index = tableIndexes[new ObjectRef("dbo", "Orders", DatabaseObjectKind.Table)][0];
        Assert.True(index.Columns.Single(c => c.ColumnName == "Comment").IsIncluded);
        Assert.False(index.Columns.Single(c => c.ColumnName == "Name").IsIncluded);
    }

    [Fact]
    public async Task ReadAsync_IndexOnView_IsReturnedAsViewIndexNotTableIndex()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult(
        [
            Row("IX_OrdersView_Id", true, "CLUSTERED", false, false, null, "Id", false, false, parentName: "OrdersView", parentTypeCode: "V")
        ]);

        var (tableIndexes, viewIndexes) = await new IndexReader().ReadAsync(runner, CancellationToken.None);

        Assert.Empty(tableIndexes);
        var viewIndex = Assert.Single(viewIndexes);
        Assert.Equal(new ObjectRef("dbo", "OrdersView", DatabaseObjectKind.View), viewIndex.View);
        Assert.Equal("IX_OrdersView_Id", viewIndex.Index.Name);
        Assert.True(viewIndex.Index.IsUnique);
    }
}
