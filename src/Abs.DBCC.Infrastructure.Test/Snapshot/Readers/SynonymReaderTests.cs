using Abs.DBCC.Infrastructure.Snapshot.Readers;
using Abs.DBCC.TestCommon.Fakes;

namespace Abs.DBCC.Infrastructure.Test.Snapshot.Readers;

public class SynonymReaderTests
{
    [Fact]
    public async Task ReadAsync_MapsBaseObjectName()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult(
        [
            new Dictionary<string, object?> { ["SchemaName"] = "dbo", ["SynonymName"] = "OrdersSyn", ["BaseObjectName"] = "OtherDb.dbo.Orders" }
        ]);

        var result = await new SynonymReader().ReadAsync(runner, CancellationToken.None);

        var syn = Assert.Single(result);
        Assert.Equal("OrdersSyn", syn.Ref.Name);
        Assert.Equal("OtherDb.dbo.Orders", syn.BaseObjectName);
    }
}
