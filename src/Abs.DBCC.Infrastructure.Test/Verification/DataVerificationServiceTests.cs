using System.Data;
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
    private static readonly ConnectionProfile Profile = new("server", "db", "user", "pw");
    private static readonly ObjectRef OrdersRef = new("dbo", "Orders", DatabaseObjectKind.Table);

    private static (DataVerificationService Sut, FakeSqlScriptRunner Runner) CreateSut()
    {
        var runner = new FakeSqlScriptRunner();
        var factory = new Mock<ISqlScriptRunnerFactory>();
        factory.Setup(f => f.CreateAsync(Profile, It.IsAny<CancellationToken>())).ReturnsAsync(runner);

        return (new DataVerificationService(factory.Object), runner);
    }

    private static DatabaseSnapshot OneTableSnapshot() =>
        new DatabaseSnapshotBuilder().WithTable(new TableSnapshotBuilder("dbo", "Orders").WithColumn("Id", "int", null).Build()).Build();

    private static Dictionary<string, object?> Row(int id, string name) => new() { ["Id"] = id, ["Name"] = name };

    private static async Task<IReadOnlyList<TableRowsSnapshot>> CaptureAsync(
        DataVerificationService sut, FakeSqlScriptRunner runner, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        runner.EnqueueQueryResult(rows);
        return await sut.CaptureRowsAsync(Profile, OneTableSnapshot());
    }

    [Fact]
    public async Task Compare_IdenticalRows_ProducesNoDiff()
    {
        var (sut, runner) = CreateSut();
        var before = await CaptureAsync(sut, runner, [Row(1, "Alice"), Row(2, "Bob")]);
        var after = await CaptureAsync(sut, runner, [Row(1, "Alice"), Row(2, "Bob")]);

        var diffs = sut.Compare(before, after);

        Assert.Empty(diffs);
    }

    [Fact]
    public async Task Compare_RowsInDifferentOrder_ProducesNoDiff()
    {
        // A heap table's physical row order is not guaranteed to be the same between the "before" and
        // "after" reads - the content fingerprint must be order-independent.
        var (sut, runner) = CreateSut();
        var before = await CaptureAsync(sut, runner, [Row(1, "Alice"), Row(2, "Bob")]);
        var after = await CaptureAsync(sut, runner, [Row(2, "Bob"), Row(1, "Alice")]);

        var diffs = sut.Compare(before, after);

        Assert.Empty(diffs);
    }

    [Fact]
    public async Task Compare_ChangedCellValue_IsReported()
    {
        var (sut, runner) = CreateSut();
        var before = await CaptureAsync(sut, runner, [Row(1, "Alice")]);
        var after = await CaptureAsync(sut, runner, [Row(1, "Alicia")]);

        var diffs = sut.Compare(before, after);

        Assert.Single(diffs);
    }

    [Fact]
    public async Task Compare_DifferentRowCount_IsReported()
    {
        var (sut, runner) = CreateSut();
        var before = await CaptureAsync(sut, runner, [Row(1, "Alice")]);
        var after = await CaptureAsync(sut, runner, [Row(1, "Alice"), Row(2, "Bob")]);

        var diffs = sut.Compare(before, after);

        Assert.Single(diffs);
        Assert.Contains("Zeilenanzahl", diffs[0].Details);
    }

    [Fact]
    public async Task Compare_EqualByteArrays_AreNotReportedAsDifferent()
    {
        var (sut, runner) = CreateSut();
        var before = await CaptureAsync(sut, runner, [new Dictionary<string, object?> { ["Data"] = new byte[] { 1, 2, 3 } }]);
        var after = await CaptureAsync(sut, runner, [new Dictionary<string, object?> { ["Data"] = new byte[] { 1, 2, 3 } }]);

        var diffs = sut.Compare(before, after);

        Assert.Empty(diffs);
    }

    [Fact]
    public void Compare_TableMissingAfterMigration_IsReported()
    {
        var before = new List<TableRowsSnapshot> { new(OrdersRef, 1, 12345UL) };
        var after = new List<TableRowsSnapshot>();
        var (sut, _) = CreateSut();

        var diffs = sut.Compare(before, after);

        Assert.Single(diffs);
    }

    [Fact]
    public async Task Compare_ColumnValuesContainingDelimiterLikeCharacters_AreDistinguishedCorrectly()
    {
        // A naive "Key=Value" string joined with "|" (no escaping) can make two genuinely different
        // rows collide onto an identical string when a value itself contains "|" or "=" - e.g.
        // A="x|B=y", B="z" joins to the exact same string as A="x", B="y|B=z". The fingerprint's
        // explicit length-prefixed encoding must tell these apart.
        var (sut, runner) = CreateSut();
        var before = await CaptureAsync(sut, runner, [new Dictionary<string, object?> { ["A"] = "x|B=y", ["B"] = "z" }]);
        var after = await CaptureAsync(sut, runner, [new Dictionary<string, object?> { ["A"] = "x", ["B"] = "y|B=z" }]);

        var diffs = sut.Compare(before, after);

        Assert.Single(diffs);
    }

    [Fact]
    public async Task Compare_DateTimeValuesDifferingOnlyInSubSecondPrecision_IsReported()
    {
        // DateTime.ToString() with no explicit format omits fractional seconds entirely, so a naive
        // ToString()-based encoding would make 12:00:00.100 and 12:00:00.900 (a real datetime2 value
        // change) hash identically and silently hide the difference.
        var (sut, runner) = CreateSut();
        var before = await CaptureAsync(sut, runner, [new Dictionary<string, object?> { ["At"] = new DateTime(2026, 1, 1, 12, 0, 0, 100) }]);
        var after = await CaptureAsync(sut, runner, [new Dictionary<string, object?> { ["At"] = new DateTime(2026, 1, 1, 12, 0, 0, 900) }]);

        var diffs = sut.Compare(before, after);

        Assert.Single(diffs);
    }

    [Fact]
    public async Task CaptureRowsAsync_SnapshotIsolationEnabled_WrapsTheWholeCaptureInASnapshotTransaction()
    {
        // Reproduces a real gap: a multi-table capture is several independent SELECTs, so a concurrent
        // write from another session in between two of them (nothing enforces exclusive access) could
        // be misreported as a migration-caused change. When the database supports it, the whole capture
        // must run under one SNAPSHOT-isolated transaction instead, giving every table the same
        // consistent point-in-time view without blocking (or being blocked by) other sessions.
        var (sut, runner) = CreateSut();
        runner.EnqueueScalarResult(1); // snapshot_isolation_state = 1 (ON)
        runner.EnqueueQueryResult([Row(1, "Alice")]);

        await sut.CaptureRowsAsync(Profile, OneTableSnapshot());

        Assert.Equal(IsolationLevel.Snapshot, runner.LastRequestedIsolationLevel);
        Assert.True(runner.WasCommitted);
    }

    [Fact]
    public async Task CaptureRowsAsync_SnapshotIsolationNotEnabled_FallsBackToPlainReadsWithoutATransaction()
    {
        var (sut, runner) = CreateSut();
        runner.EnqueueScalarResult(0); // snapshot_isolation_state = 0 (OFF)
        runner.EnqueueQueryResult([Row(1, "Alice")]);

        await sut.CaptureRowsAsync(Profile, OneTableSnapshot());

        Assert.Null(runner.LastRequestedIsolationLevel);
        Assert.False(runner.WasCommitted);
        Assert.False(runner.IsInTransaction);
    }
}
