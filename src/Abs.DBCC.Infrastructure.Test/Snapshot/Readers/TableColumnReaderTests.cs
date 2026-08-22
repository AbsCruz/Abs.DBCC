using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Snapshot.Readers;
using Abs.DBCC.TestCommon.Fakes;

namespace Abs.DBCC.Infrastructure.Test.Snapshot.Readers;

public class TableColumnReaderTests
{
    private static Dictionary<string, object?> Row(
        string schema, string table, string column, string type, short? maxLength, string? collation,
        bool isComputed = false, string? computedDefinition = null, bool? isPersisted = null) =>
        new()
        {
            ["SchemaName"] = schema,
            ["TableName"] = table,
            ["ColumnId"] = (short)1,
            ["ColumnName"] = column,
            ["SqlDataType"] = type,
            ["MaxLength"] = maxLength,
            ["Precision"] = (byte?)null,
            ["Scale"] = (byte?)null,
            ["IsNullable"] = true,
            ["CollationName"] = collation,
            ["IsComputed"] = isComputed,
            ["ComputedDefinition"] = computedDefinition,
            ["IsComputedPersisted"] = isPersisted
        };

    [Fact]
    public async Task ReadAsync_GroupsColumnsByTable()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult(
        [
            Row("dbo", "Orders", "Id", "int", null, null),
            Row("dbo", "Orders", "Name", "nvarchar", 100, "Latin1_General_CI_AS"),
            Row("dbo", "Customers", "Email", "varchar", 200, "Latin1_General_CI_AS")
        ]);

        var result = await new TableColumnReader().ReadAsync(runner, CancellationToken.None);

        Assert.Equal(2, result.Count);
        var orders = result[new ObjectRef("dbo", "Orders", DatabaseObjectKind.Table)];
        Assert.Equal(2, orders.Count);
        Assert.False(orders[0].IsCharacterType);
        Assert.True(orders[1].IsCharacterType);
        Assert.Equal("Latin1_General_CI_AS", orders[1].Collation!.Value);
    }

    [Fact]
    public async Task ReadAsync_MapsComputedColumnDefinition()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult(
        [
            Row("dbo", "Orders", "FullName", "nvarchar", null, null,
                isComputed: true, computedDefinition: "[First]+' '+[Last]", isPersisted: true)
        ]);

        var result = await new TableColumnReader().ReadAsync(runner, CancellationToken.None);

        var column = result[new ObjectRef("dbo", "Orders", DatabaseObjectKind.Table)][0];
        Assert.True(column.IsComputed);
        Assert.Equal("[First]+' '+[Last]", column.ComputedDefinition);
        Assert.True(column.IsComputedPersisted);
        Assert.False(column.IsCharacterType, "computed columns are never ALTER COLUMN candidates");
    }
}
