using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Migration.DdlScriptGenerators;

namespace Abs.DBCC.Infrastructure.Test.Migration.DdlScriptGenerators;

public class IndexScriptGeneratorTests
{
    private static readonly ObjectRef Table = new("dbo", "Orders", DatabaseObjectKind.Table);

    [Fact]
    public void GenerateDrop_PlainIndex_UsesDropIndexOnTable()
    {
        var index = new IndexSnapshot("IX_Orders_Name", false, false, false, false, [], null);

        var sql = IndexScriptGenerator.GenerateDrop(Table, index);

        Assert.Equal("DROP INDEX [IX_Orders_Name] ON [dbo].[Orders];", sql);
    }

    [Fact]
    public void GenerateDrop_PrimaryKey_UsesDropConstraint()
    {
        var index = new IndexSnapshot("PK_Orders", true, true, true, false, [], null);

        var sql = IndexScriptGenerator.GenerateDrop(Table, index);

        Assert.Equal("ALTER TABLE [dbo].[Orders] DROP CONSTRAINT [PK_Orders];", sql);
    }

    [Fact]
    public void GenerateCreate_PrimaryKey_UsesAddConstraint()
    {
        var index = new IndexSnapshot("PK_Orders", true, true, true, false,
            [new IndexColumnSnapshot("Id", false, false)], null);

        var sql = IndexScriptGenerator.GenerateCreate(Table, index);

        Assert.Equal("ALTER TABLE [dbo].[Orders] ADD CONSTRAINT [PK_Orders] PRIMARY KEY CLUSTERED ([Id] ASC);", sql);
    }

    [Fact]
    public void GenerateCreate_UniqueConstraint_UsesUniqueKeyword()
    {
        var index = new IndexSnapshot("UQ_Orders_Number", true, false, false, true,
            [new IndexColumnSnapshot("Number", false, false)], null);

        var sql = IndexScriptGenerator.GenerateCreate(Table, index);

        Assert.Equal("ALTER TABLE [dbo].[Orders] ADD CONSTRAINT [UQ_Orders_Number] UNIQUE NONCLUSTERED ([Number] ASC);", sql);
    }

    [Fact]
    public void GenerateCreate_PlainIndex_WithIncludeAndFilter()
    {
        var index = new IndexSnapshot("IX_Orders_Name", false, false, false, false,
            [
                new IndexColumnSnapshot("Name", false, false),
                new IndexColumnSnapshot("Total", true, false),
                new IndexColumnSnapshot("Comment", false, true)
            ],
            "([IsActive]=(1))");

        var sql = IndexScriptGenerator.GenerateCreate(Table, index);

        Assert.Equal(
            "CREATE NONCLUSTERED INDEX [IX_Orders_Name] ON [dbo].[Orders] ([Name] ASC, [Total] DESC) INCLUDE ([Comment]) WHERE ([IsActive]=(1));",
            sql);
    }

    [Fact]
    public void GenerateCreate_UniqueIndex_IncludesUniqueKeyword()
    {
        var index = new IndexSnapshot("IX_Orders_Number", true, false, false, false,
            [new IndexColumnSnapshot("Number", false, false)], null);

        var sql = IndexScriptGenerator.GenerateCreate(Table, index);

        Assert.StartsWith("CREATE UNIQUE NONCLUSTERED INDEX", sql);
    }
}
