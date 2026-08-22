using Abs.DBCC.Infrastructure.Snapshot.Readers;
using Abs.DBCC.TestCommon.Fakes;

namespace Abs.DBCC.Infrastructure.Test.Snapshot.Readers;

public class SequenceReaderTests
{
    [Fact]
    public async Task ReadAsync_MapsSequenceProperties()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult(
        [
            new Dictionary<string, object?>
            {
                ["SchemaName"] = "dbo", ["SequenceName"] = "OrderNumbers", ["DataType"] = "bigint",
                ["StartValue"] = "1", ["Increment"] = "1", ["MinValue"] = "1", ["MaxValue"] = "9223372036854775807",
                ["IsCycling"] = false, ["CacheSize"] = 50
            }
        ]);

        var result = await new SequenceReader().ReadAsync(runner, CancellationToken.None);

        var seq = Assert.Single(result);
        Assert.Equal("OrderNumbers", seq.Ref.Name);
        Assert.Equal("1", seq.StartValue);
        Assert.False(seq.IsCycling);
        Assert.Equal(50, seq.CacheSize);
    }
}
