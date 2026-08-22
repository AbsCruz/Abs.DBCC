using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Migration.DdlScriptGenerators;

namespace Abs.DBCC.Infrastructure.Test.Migration.DdlScriptGenerators;

public class ExtendedPropertyScriptGeneratorTests
{
    private static readonly ObjectRef Table = new("dbo", "Orders", DatabaseObjectKind.Table);

    [Fact]
    public void GenerateAdd_ObjectLevel_OmitsLevel2Clause()
    {
        var property = new ExtendedPropertySnapshot(Table, null, "MS_Description", "Order header table");

        var sql = ExtendedPropertyScriptGenerator.GenerateAdd(property);

        Assert.Equal(
            "EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'Order header table', " +
            "@level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Orders';",
            sql);
    }

    [Fact]
    public void GenerateAdd_ColumnLevel_IncludesLevel2Clause()
    {
        var property = new ExtendedPropertySnapshot(Table, "CustomerName", "MS_Description", "Customer full name");

        var sql = ExtendedPropertyScriptGenerator.GenerateAdd(property);

        Assert.Equal(
            "EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'Customer full name', " +
            "@level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Orders', " +
            "@level2type = N'COLUMN', @level2name = N'CustomerName';",
            sql);
    }

    [Fact]
    public void GenerateAdd_EscapesEmbeddedSingleQuotes()
    {
        var property = new ExtendedPropertySnapshot(Table, null, "MS_Description", "Customer's order");

        var sql = ExtendedPropertyScriptGenerator.GenerateAdd(property);

        Assert.Contains("N'Customer''s order'", sql);
    }

    [Theory]
    [InlineData(DatabaseObjectKind.View, "VIEW")]
    [InlineData(DatabaseObjectKind.StoredProcedure, "PROCEDURE")]
    [InlineData(DatabaseObjectKind.Function, "FUNCTION")]
    public void GenerateAdd_UsesCorrectLevel1TypePerObjectKind(DatabaseObjectKind kind, string expectedLevel1Type)
    {
        var property = new ExtendedPropertySnapshot(new ObjectRef("dbo", "Thing", kind), null, "MS_Description", "value");

        var sql = ExtendedPropertyScriptGenerator.GenerateAdd(property);

        Assert.Contains($"@level1type = N'{expectedLevel1Type}'", sql);
    }

    [Fact]
    public void GenerateAdd_OnCheckConstraint_UsesConstraintLevel2TypeAndParentTableAsLevel1()
    {
        var constraint = new ObjectRef("dbo", "CK_Orders_Amount", DatabaseObjectKind.CheckConstraint);
        var property = new ExtendedPropertySnapshot(constraint, null, "MS_Description", "must be positive", ParentTable: Table);

        var sql = ExtendedPropertyScriptGenerator.GenerateAdd(property);

        Assert.Equal(
            "EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'must be positive', " +
            "@level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'Orders', " +
            "@level2type = N'CONSTRAINT', @level2name = N'CK_Orders_Amount';",
            sql);
    }

    [Theory]
    [InlineData(DatabaseObjectKind.PrimaryKey)]
    [InlineData(DatabaseObjectKind.UniqueConstraint)]
    [InlineData(DatabaseObjectKind.CheckConstraint)]
    [InlineData(DatabaseObjectKind.DefaultConstraint)]
    [InlineData(DatabaseObjectKind.ForeignKey)]
    public void GenerateAdd_OnAnyConstraintKind_UsesConstraintLevel2Type(DatabaseObjectKind kind)
    {
        var constraint = new ObjectRef("dbo", "SomeConstraint", kind);
        var property = new ExtendedPropertySnapshot(constraint, null, "P", "v", ParentTable: Table);

        var sql = ExtendedPropertyScriptGenerator.GenerateAdd(property);

        Assert.Contains("@level2type = N'CONSTRAINT'", sql);
    }

    [Fact]
    public void GenerateAdd_OnIndex_UsesIndexLevel2Type()
    {
        var index = new ObjectRef("dbo", "IX_Orders_Name", DatabaseObjectKind.Index);
        var property = new ExtendedPropertySnapshot(index, null, "MS_Description", "speeds up lookups", ParentTable: Table);

        var sql = ExtendedPropertyScriptGenerator.GenerateAdd(property);

        Assert.Contains("@level2type = N'INDEX', @level2name = N'IX_Orders_Name'", sql);
    }

    [Fact]
    public void GenerateAdd_OnIndexOfIndexedView_UsesViewAsLevel1()
    {
        var view = new ObjectRef("dbo", "OrdersView", DatabaseObjectKind.View);
        var index = new ObjectRef("dbo", "IX_OrdersView_Id", DatabaseObjectKind.Index);
        var property = new ExtendedPropertySnapshot(index, null, "MS_Description", "clustered index", ParentTable: view);

        var sql = ExtendedPropertyScriptGenerator.GenerateAdd(property);

        Assert.Contains("@level1type = N'VIEW', @level1name = N'OrdersView'", sql);
        Assert.Contains("@level2type = N'INDEX', @level2name = N'IX_OrdersView_Id'", sql);
    }
}
