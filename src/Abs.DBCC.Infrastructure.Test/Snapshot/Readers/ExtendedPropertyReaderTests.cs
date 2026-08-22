using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Snapshot.Readers;
using Abs.DBCC.TestCommon.Fakes;

namespace Abs.DBCC.Infrastructure.Test.Snapshot.Readers;

public class ExtendedPropertyReaderTests
{
    private static Dictionary<string, object?> ObjectOrColumnRow(
        string objectName, string typeCode, string? columnName, string propertyName, string propertyValue,
        string? parentSchemaName = null, string? parentTableName = null) =>
        new()
        {
            ["SchemaName"] = "dbo",
            ["ObjectName"] = objectName,
            ["ObjectTypeCode"] = typeCode,
            ["ColumnName"] = columnName,
            ["PropertyName"] = propertyName,
            ["PropertyValue"] = propertyValue,
            ["ParentSchemaName"] = parentSchemaName,
            ["ParentTableName"] = parentTableName
        };

    private static Dictionary<string, object?> IndexRow(string parentName, string parentTypeCode, string indexName, string propertyName, string propertyValue) =>
        new()
        {
            ["SchemaName"] = "dbo",
            ["ParentName"] = parentName,
            ["ParentTypeCode"] = parentTypeCode,
            ["IndexName"] = indexName,
            ["PropertyName"] = propertyName,
            ["PropertyValue"] = propertyValue
        };

    [Fact]
    public async Task ReadAsync_MapsTableAndColumnLevelProperties()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult(
        [
            ObjectOrColumnRow("Orders", "U", null, "MS_Description", "Order header table"),
            ObjectOrColumnRow("Orders", "U", "CustomerName", "MS_Description", "Customer full name")
        ]);
        runner.EnqueueQueryResult([]); // index-scoped

        var result = await new ExtendedPropertyReader().ReadAsync(runner, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, p => p.ColumnName is null && p.PropertyValue == "Order header table" && p.ParentTable is null);
        Assert.Contains(result, p => p.ColumnName == "CustomerName" && p.PropertyValue == "Customer full name");
    }

    [Fact]
    public async Task ReadAsync_SkipsUnmappedObjectTypes()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult(
        [
            ObjectOrColumnRow("SomeQueue", "SQ", null, "MS_Description", "n/a")
        ]);
        runner.EnqueueQueryResult([]);

        var result = await new ExtendedPropertyReader().ReadAsync(runner, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadAsync_ConstraintScopedProperty_ResolvesParentTable()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult(
        [
            ObjectOrColumnRow("CK_Orders_Amount", "C", null, "MS_Description", "Amount must be positive",
                parentSchemaName: "dbo", parentTableName: "Orders")
        ]);
        runner.EnqueueQueryResult([]);

        var result = await new ExtendedPropertyReader().ReadAsync(runner, CancellationToken.None);

        var prop = Assert.Single(result);
        Assert.Equal(DatabaseObjectKind.CheckConstraint, prop.Object.Kind);
        Assert.Equal("CK_Orders_Amount", prop.Object.Name);
        Assert.Equal(new ObjectRef("dbo", "Orders", DatabaseObjectKind.Table), prop.ParentTable);
    }

    [Theory]
    [InlineData("PK", DatabaseObjectKind.PrimaryKey)]
    [InlineData("UQ", DatabaseObjectKind.UniqueConstraint)]
    [InlineData("D", DatabaseObjectKind.DefaultConstraint)]
    [InlineData("F", DatabaseObjectKind.ForeignKey)]
    public async Task ReadAsync_MapsAllConstraintTypeCodes(string typeCode, DatabaseObjectKind expectedKind)
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult(
        [
            ObjectOrColumnRow("Constraint1", typeCode, null, "P", "v", parentSchemaName: "dbo", parentTableName: "Orders")
        ]);
        runner.EnqueueQueryResult([]);

        var result = await new ExtendedPropertyReader().ReadAsync(runner, CancellationToken.None);

        Assert.Equal(expectedKind, Assert.Single(result).Object.Kind);
    }

    [Fact]
    public async Task ReadAsync_IndexScopedProperty_ResolvesParentTableAndIndex()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult([]);
        runner.EnqueueQueryResult(
        [
            IndexRow("Orders", "U", "IX_Orders_Name", "MS_Description", "Speeds up name lookups")
        ]);

        var result = await new ExtendedPropertyReader().ReadAsync(runner, CancellationToken.None);

        var prop = Assert.Single(result);
        Assert.Equal(DatabaseObjectKind.Index, prop.Object.Kind);
        Assert.Equal("IX_Orders_Name", prop.Object.Name);
        Assert.Equal(new ObjectRef("dbo", "Orders", DatabaseObjectKind.Table), prop.ParentTable);
    }

    [Fact]
    public async Task ReadAsync_IndexScopedProperty_OnIndexedView_ResolvesParentAsView()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult([]);
        runner.EnqueueQueryResult(
        [
            IndexRow("OrdersView", "V", "IX_OrdersView_Id", "MS_Description", "Clustered index of the indexed view")
        ]);

        var result = await new ExtendedPropertyReader().ReadAsync(runner, CancellationToken.None);

        Assert.Equal(new ObjectRef("dbo", "OrdersView", DatabaseObjectKind.View), Assert.Single(result).ParentTable);
    }
}
