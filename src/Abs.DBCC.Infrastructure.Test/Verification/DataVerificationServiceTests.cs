using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Verification;
using Moq;

namespace Abs.DBCC.Infrastructure.Test.Verification;

public class DataVerificationServiceTests
{
    private static readonly ObjectRef OrdersRef = new("dbo", "Orders", DatabaseObjectKind.Table);
    private readonly DataVerificationService _sut = new(Mock.Of<ISqlScriptRunnerFactory>());

    private static Dictionary<string, object?> Row(int id, string name) => new() { ["Id"] = id, ["Name"] = name };

    [Fact]
    public void Compare_IdenticalRows_ProducesNoDiff()
    {
        var rows = new List<IReadOnlyDictionary<string, object?>> { Row(1, "Alice"), Row(2, "Bob") };
        var before = new List<TableRowsSnapshot> { new(OrdersRef, rows) };
        var after = new List<TableRowsSnapshot> { new(OrdersRef, rows) };

        var diffs = _sut.Compare(before, after);

        Assert.Empty(diffs);
    }

    [Fact]
    public void Compare_RowsInDifferentOrder_ProducesNoDiff()
    {
        var before = new List<TableRowsSnapshot>
        {
            new(OrdersRef, [Row(1, "Alice"), Row(2, "Bob")])
        };
        var after = new List<TableRowsSnapshot>
        {
            new(OrdersRef, [Row(2, "Bob"), Row(1, "Alice")])
        };

        var diffs = _sut.Compare(before, after);

        Assert.Empty(diffs);
    }

    [Fact]
    public void Compare_ChangedCellValue_IsReported()
    {
        var before = new List<TableRowsSnapshot> { new(OrdersRef, [Row(1, "Alice")]) };
        var after = new List<TableRowsSnapshot> { new(OrdersRef, [Row(1, "Alicia")]) };

        var diffs = _sut.Compare(before, after);

        Assert.Single(diffs);
    }

    [Fact]
    public void Compare_DifferentRowCount_IsReported()
    {
        var before = new List<TableRowsSnapshot> { new(OrdersRef, [Row(1, "Alice")]) };
        var after = new List<TableRowsSnapshot> { new(OrdersRef, [Row(1, "Alice"), Row(2, "Bob")]) };

        var diffs = _sut.Compare(before, after);

        Assert.Single(diffs);
        Assert.Contains("Zeilenanzahl", diffs[0].Details);
    }

    [Fact]
    public void Compare_EqualByteArrays_AreNotReportedAsDifferent()
    {
        var before = new List<TableRowsSnapshot>
        {
            new(OrdersRef, [new Dictionary<string, object?> { ["Data"] = new byte[] { 1, 2, 3 } }])
        };
        var after = new List<TableRowsSnapshot>
        {
            new(OrdersRef, [new Dictionary<string, object?> { ["Data"] = new byte[] { 1, 2, 3 } }])
        };

        var diffs = _sut.Compare(before, after);

        Assert.Empty(diffs);
    }

    [Fact]
    public void Compare_TableMissingAfterMigration_IsReported()
    {
        var before = new List<TableRowsSnapshot> { new(OrdersRef, [Row(1, "Alice")]) };
        var after = new List<TableRowsSnapshot>();

        var diffs = _sut.Compare(before, after);

        Assert.Single(diffs);
    }
}
