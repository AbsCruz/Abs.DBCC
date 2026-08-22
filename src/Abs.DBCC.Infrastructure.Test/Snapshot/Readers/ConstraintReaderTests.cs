using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Snapshot.Readers;
using Abs.DBCC.TestCommon.Fakes;

namespace Abs.DBCC.Infrastructure.Test.Snapshot.Readers;

public class ConstraintReaderTests
{
    private static readonly ObjectRef OrdersRef = new("dbo", "Orders", DatabaseObjectKind.Table);

    [Fact]
    public async Task ReadAsync_MapsColumnLevelAndTableLevelCheckConstraints()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult(
        [
            new Dictionary<string, object?> { ["SchemaName"] = "dbo", ["TableName"] = "Orders", ["ConstraintName"] = "CK_Amount", ["ColumnName"] = "Amount", ["Definition"] = "([Amount]>(0))" },
            new Dictionary<string, object?> { ["SchemaName"] = "dbo", ["TableName"] = "Orders", ["ConstraintName"] = "CK_Multi", ["ColumnName"] = null, ["Definition"] = "([Start]<[End])" }
        ]);
        runner.EnqueueQueryResult([]);

        var (checks, defaults) = await new ConstraintReader().ReadAsync(runner, CancellationToken.None);

        var orderChecks = checks[OrdersRef];
        Assert.Equal(2, orderChecks.Count);
        Assert.Equal("Amount", orderChecks.Single(c => c.Name == "CK_Amount").ColumnName);
        Assert.Null(orderChecks.Single(c => c.Name == "CK_Multi").ColumnName);
        Assert.Empty(defaults);
    }

    [Fact]
    public async Task ReadAsync_MapsDefaultConstraints()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult([]);
        runner.EnqueueQueryResult(
        [
            new Dictionary<string, object?> { ["SchemaName"] = "dbo", ["TableName"] = "Orders", ["ConstraintName"] = "DF_Status", ["ColumnName"] = "Status", ["Definition"] = "('Pending')" }
        ]);

        var (checks, defaults) = await new ConstraintReader().ReadAsync(runner, CancellationToken.None);

        var orderDefaults = defaults[OrdersRef];
        var def = Assert.Single(orderDefaults);
        Assert.Equal("Status", def.ColumnName);
        Assert.Equal("('Pending')", def.Definition);
        Assert.Empty(checks);
    }
}
