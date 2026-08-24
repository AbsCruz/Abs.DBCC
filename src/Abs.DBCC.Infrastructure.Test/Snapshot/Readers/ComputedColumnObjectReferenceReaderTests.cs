using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Snapshot.Readers;
using Abs.DBCC.TestCommon.Fakes;

namespace Abs.DBCC.Infrastructure.Test.Snapshot.Readers;

public class ComputedColumnObjectReferenceReaderTests
{
    [Fact]
    public async Task ReadAsync_MapsComputedColumnReferencingSchemaBoundFunction()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult(
        [
            new Dictionary<string, object?>
            {
                ["TableSchema"] = "dbo", ["TableName"] = "RegistrationLog", ["ColumnName"] = "IsValidFlag",
                ["ReferencedSchema"] = "dbo", ["ReferencedName"] = "GetMinRegistrationDate", ["ReferencedTypeCode"] = "FN"
            }
        ]);

        var result = await new ComputedColumnObjectReferenceReader().ReadAsync(runner, CancellationToken.None);

        var reference = Assert.Single(result);
        Assert.Equal("dbo", reference.Table.SchemaName);
        Assert.Equal("RegistrationLog", reference.Table.Name);
        Assert.Equal("IsValidFlag", reference.ColumnName);
        Assert.Equal(DatabaseObjectKind.Function, reference.ReferencedObject.Kind);
        Assert.Equal("GetMinRegistrationDate", reference.ReferencedObject.Name);
    }

    [Fact]
    public async Task ReadAsync_MapsComputedColumnReferencingSchemaBoundView()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult(
        [
            new Dictionary<string, object?>
            {
                ["TableSchema"] = "dbo", ["TableName"] = "Orders", ["ColumnName"] = "Summary",
                ["ReferencedSchema"] = "dbo", ["ReferencedName"] = "OrdersView", ["ReferencedTypeCode"] = "V"
            }
        ]);

        var result = await new ComputedColumnObjectReferenceReader().ReadAsync(runner, CancellationToken.None);

        Assert.Equal(DatabaseObjectKind.View, result.Single().ReferencedObject.Kind);
    }
}
