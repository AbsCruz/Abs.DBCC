using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Migration.DdlScriptGenerators;

namespace Abs.DBCC.Infrastructure.Test.Migration.DdlScriptGenerators;

public class FullTextIndexScriptGeneratorTests
{
    private static readonly ObjectRef Table = new("dbo", "Articles", DatabaseObjectKind.Table);

    [Fact]
    public void GenerateDrop_DropsFullTextIndexOnTable()
    {
        var index = new FullTextIndexSnapshot(Table, "MainCatalog", "PK_Articles", "AUTO", [new FullTextIndexColumnSnapshot("Title", 1033)]);

        var sql = FullTextIndexScriptGenerator.GenerateDrop(index);

        Assert.Equal("DROP FULLTEXT INDEX ON [dbo].[Articles];", sql);
    }

    [Fact]
    public void GenerateCreate_IncludesColumnsLanguageKeyIndexAndCatalog()
    {
        var index = new FullTextIndexSnapshot(
            Table, "MainCatalog", "PK_Articles", "AUTO",
            [new FullTextIndexColumnSnapshot("Title", 1033), new FullTextIndexColumnSnapshot("Body", 1033)]);

        var sql = FullTextIndexScriptGenerator.GenerateCreate(index);

        Assert.Equal(
            "CREATE FULLTEXT INDEX ON [dbo].[Articles] ([Title] LANGUAGE 1033, [Body] LANGUAGE 1033) " +
            "KEY INDEX [PK_Articles] ON [MainCatalog] WITH CHANGE_TRACKING AUTO;",
            sql);
    }
}
