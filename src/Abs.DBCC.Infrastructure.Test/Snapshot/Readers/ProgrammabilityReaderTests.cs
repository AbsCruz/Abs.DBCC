using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Snapshot.Readers;
using Abs.DBCC.TestCommon.Fakes;

namespace Abs.DBCC.Infrastructure.Test.Snapshot.Readers;

public class ProgrammabilityReaderTests
{
    private static Dictionary<string, object?> Row(string schema, string name, string typeCode, string definition, int? isSchemaBound) =>
        new()
        {
            ["SchemaName"] = schema,
            ["ObjectName"] = name,
            ["ObjectTypeCode"] = typeCode,
            ["Definition"] = definition,
            ["IsSchemaBound"] = isSchemaBound
        };

    [Theory]
    [InlineData("V", DatabaseObjectKind.View)]
    [InlineData("P", DatabaseObjectKind.StoredProcedure)]
    [InlineData("FN", DatabaseObjectKind.Function)]
    [InlineData("IF", DatabaseObjectKind.Function)]
    [InlineData("TF", DatabaseObjectKind.Function)]
    [InlineData("TR", DatabaseObjectKind.Trigger)]
    public async Task ReadAsync_MapsTypeCodeToKind(string typeCode, DatabaseObjectKind expectedKind)
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult([Row("dbo", "Thing", typeCode, "CREATE ...", 0)]);

        var result = await new ProgrammabilityReader().ReadAsync(runner, CancellationToken.None);

        Assert.Equal(expectedKind, result.Single().Ref.Kind);
    }

    [Fact]
    public async Task ReadAsync_MapsSchemaBoundFlag()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult(
        [
            Row("dbo", "BoundView", "V", "CREATE VIEW dbo.BoundView WITH SCHEMABINDING AS SELECT 1;", 1),
            Row("dbo", "PlainView", "V", "CREATE VIEW dbo.PlainView AS SELECT 1;", 0)
        ]);

        var result = await new ProgrammabilityReader().ReadAsync(runner, CancellationToken.None);

        Assert.True(result.Single(o => o.Ref.Name == "BoundView").IsSchemaBound);
        Assert.False(result.Single(o => o.Ref.Name == "PlainView").IsSchemaBound);
    }

    [Fact]
    public async Task ReadAsync_CapturesDefinitionVerbatim()
    {
        var runner = new FakeSqlScriptRunner();
        const string definition = "CREATE PROCEDURE dbo.DoStuff AS BEGIN SELECT 1; END";
        runner.EnqueueQueryResult([Row("dbo", "DoStuff", "P", definition, 0)]);

        var result = await new ProgrammabilityReader().ReadAsync(runner, CancellationToken.None);

        Assert.Equal(definition, result.Single().DefinitionScript);
    }
}
