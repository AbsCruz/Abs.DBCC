using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Verification;
using Abs.DBCC.TestCommon.Builders;
using Abs.DBCC.TestCommon.Fakes;
using Moq;

namespace Abs.DBCC.Infrastructure.Test.Verification;

public class DataVerificationServiceTests
{
    private static readonly ObjectRef OrdersRef = new("dbo", "Orders", DatabaseObjectKind.Table);
    private static readonly ObjectRef LogsRef = new("dbo", "Logs", DatabaseObjectKind.Table);
    private static readonly ConnectionProfile Profile = new("server", "db", "user", "pw");
    private readonly DataVerificationService _sut = new(Mock.Of<ISqlScriptRunnerFactory>());

    private static Dictionary<string, object?> Row(int id, string name) => new() { ["Id"] = id, ["Name"] = name };

    private static TableSnapshot Table(ObjectRef table) => new(table, [], [], [], []);

    private static IReadOnlyList<string> Hashes(params IReadOnlyDictionary<string, object?>[] rows) =>
        rows.Select(RowHash.Compute).OrderBy(h => h, StringComparer.Ordinal).ToList();

    [Fact]
    public void Compare_IdenticalRows_ProducesNoDiff()
    {
        var hashes = Hashes(Row(1, "Alice"), Row(2, "Bob"));
        var before = new List<TableRowsSnapshot> { new(OrdersRef, hashes) };
        var after = new List<TableRowsSnapshot> { new(OrdersRef, hashes) };

        var diffs = _sut.Compare(before, after);

        Assert.Empty(diffs);
    }

    [Fact]
    public void Compare_RowsInDifferentOrder_ProducesNoDiff()
    {
        var before = new List<TableRowsSnapshot>
        {
            new(OrdersRef, Hashes(Row(1, "Alice"), Row(2, "Bob")))
        };
        var after = new List<TableRowsSnapshot>
        {
            new(OrdersRef, Hashes(Row(2, "Bob"), Row(1, "Alice")))
        };

        var diffs = _sut.Compare(before, after);

        Assert.Empty(diffs);
    }

    [Fact]
    public void Compare_ChangedCellValue_IsReported()
    {
        var before = new List<TableRowsSnapshot> { new(OrdersRef, Hashes(Row(1, "Alice"))) };
        var after = new List<TableRowsSnapshot> { new(OrdersRef, Hashes(Row(1, "Alicia"))) };

        var diffs = _sut.Compare(before, after);

        Assert.Single(diffs);
    }

    [Fact]
    public void Compare_DifferentRowCount_IsReported()
    {
        var before = new List<TableRowsSnapshot> { new(OrdersRef, Hashes(Row(1, "Alice"))) };
        var after = new List<TableRowsSnapshot> { new(OrdersRef, Hashes(Row(1, "Alice"), Row(2, "Bob"))) };

        var diffs = _sut.Compare(before, after);

        Assert.Single(diffs);
        Assert.Contains("Zeilenanzahl", diffs[0].Details);
    }

    [Fact]
    public void Compare_EqualByteArrays_AreNotReportedAsDifferent()
    {
        var row = new Dictionary<string, object?> { ["Data"] = new byte[] { 1, 2, 3 } };
        var rowCopy = new Dictionary<string, object?> { ["Data"] = new byte[] { 1, 2, 3 } };
        var before = new List<TableRowsSnapshot> { new(OrdersRef, Hashes(row)) };
        var after = new List<TableRowsSnapshot> { new(OrdersRef, Hashes(rowCopy)) };

        var diffs = _sut.Compare(before, after);

        Assert.Empty(diffs);
    }

    [Fact]
    public void Compare_TableMissingAfterMigration_IsReported()
    {
        var before = new List<TableRowsSnapshot> { new(OrdersRef, Hashes(Row(1, "Alice"))) };
        var after = new List<TableRowsSnapshot>();

        var diffs = _sut.Compare(before, after);

        Assert.Single(diffs);
    }

    [Fact]
    public async Task CaptureRowsAsync_ReportsTableNameAndPositionBeforeQueryingEachTable()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueStreamResult([Row(1, "Alice")]);
        runner.EnqueueStreamResult([Row(2, "Bob")]);
        var factory = new Mock<ISqlScriptRunnerFactory>();
        factory.Setup(f => f.CreateAsync(Profile, It.IsAny<CancellationToken>())).ReturnsAsync(runner);
        var snapshot = new DatabaseSnapshotBuilder().WithTable(Table(OrdersRef)).WithTable(Table(LogsRef)).Build();
        var sut = new DataVerificationService(factory.Object);
        var progress = new RecordingProgress();

        await sut.CaptureRowsAsync(Profile, snapshot, progress);

        Assert.Equal(2, progress.Reports.Count);
        Assert.Equal((1, 2, OrdersRef.ToString()), (progress.Reports[0].Completed, progress.Reports[0].Total, progress.Reports[0].CurrentTableName));
        Assert.Equal((2, 2, LogsRef.ToString()), (progress.Reports[1].Completed, progress.Reports[1].Total, progress.Reports[1].CurrentTableName));
    }

    private sealed class RecordingProgress : IProgress<TableCaptureProgress>
    {
        public List<TableCaptureProgress> Reports { get; } = [];
        public void Report(TableCaptureProgress value) => Reports.Add(value);
    }
}
