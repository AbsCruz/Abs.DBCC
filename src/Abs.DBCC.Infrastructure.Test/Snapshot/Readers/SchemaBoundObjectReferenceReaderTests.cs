using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Snapshot.Readers;
using Abs.DBCC.TestCommon.Fakes;

namespace Abs.DBCC.Infrastructure.Test.Snapshot.Readers;

public class SchemaBoundObjectReferenceReaderTests
{
    [Fact]
    public async Task ReadAsync_MapsViewReferencingAnotherView()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult(
        [
            new Dictionary<string, object?>
            {
                ["DependentSchema"] = "dbo", ["DependentName"] = "vOrdersView", ["DependentTypeCode"] = "V",
                ["ReferencedSchema"] = "dbo", ["ReferencedName"] = "_vOrdersView", ["ReferencedTypeCode"] = "V"
            }
        ]);

        var result = await new SchemaBoundObjectReferenceReader().ReadAsync(runner, CancellationToken.None);

        var reference = Assert.Single(result);
        Assert.Equal(DatabaseObjectKind.View, reference.DependentObject.Kind);
        Assert.Equal("vOrdersView", reference.DependentObject.Name);
        Assert.Equal(DatabaseObjectKind.View, reference.ReferencedObject.Kind);
        Assert.Equal("_vOrdersView", reference.ReferencedObject.Name);
    }

    [Fact]
    public async Task ReadAsync_MapsFunctionReferencingView()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult(
        [
            new Dictionary<string, object?>
            {
                ["DependentSchema"] = "dbo", ["DependentName"] = "GetOrderSummary", ["DependentTypeCode"] = "FN",
                ["ReferencedSchema"] = "dbo", ["ReferencedName"] = "_vOrdersView", ["ReferencedTypeCode"] = "V"
            }
        ]);

        var result = await new SchemaBoundObjectReferenceReader().ReadAsync(runner, CancellationToken.None);

        Assert.Equal(DatabaseObjectKind.Function, result.Single().DependentObject.Kind);
    }
}
