using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Snapshot.Readers;
using Abs.DBCC.TestCommon.Fakes;

namespace Abs.DBCC.Infrastructure.Test.Snapshot.Readers;

public class FullTextReaderTests
{
    [Fact]
    public async Task ReadCatalogsAsync_MapsNameAndDefaultFlag()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult(
        [
            new Dictionary<string, object?> { ["CatalogName"] = "MainCatalog", ["IsDefault"] = true }
        ]);

        var result = await new FullTextReader().ReadCatalogsAsync(runner, CancellationToken.None);

        var catalog = Assert.Single(result);
        Assert.Equal("MainCatalog", catalog.Name);
        Assert.True(catalog.IsDefault);
    }

    [Fact]
    public async Task ReadIndexesAsync_GroupsMultiColumnIndexIntoOneSnapshot()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult(
        [
            new Dictionary<string, object?>
            {
                ["SchemaName"] = "dbo", ["TableName"] = "Articles", ["CatalogName"] = "MainCatalog",
                ["KeyIndexName"] = "PK_Articles", ["ChangeTracking"] = "AUTO", ["ColumnName"] = "Title", ["LanguageId"] = 1033
            },
            new Dictionary<string, object?>
            {
                ["SchemaName"] = "dbo", ["TableName"] = "Articles", ["CatalogName"] = "MainCatalog",
                ["KeyIndexName"] = "PK_Articles", ["ChangeTracking"] = "AUTO", ["ColumnName"] = "Body", ["LanguageId"] = 1033
            }
        ]);

        var result = await new FullTextReader().ReadIndexesAsync(runner, CancellationToken.None);

        var index = Assert.Single(result);
        Assert.Equal(new ObjectRef("dbo", "Articles", DatabaseObjectKind.Table), index.Table);
        Assert.Equal(2, index.Columns.Count);
        Assert.True(index.CoversColumn("title"));
        Assert.False(index.CoversColumn("Other"));
    }
}
