using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Verification;

/// <summary>
/// Verifies every table's data (as .NET values read back through the data reader - the correct level to
/// compare at, since a collation change can alter a string's underlying bytes without changing its
/// logical value) without ever holding more than one table's row count and a fixed-size fingerprint in
/// memory - a table with millions of rows must not require buffering all of them, twice (once for
/// "before", once for "after"), just to verify nothing changed.
///
/// Each row is hashed via an explicit length-prefixed encoding of every column (so that, unlike a naive
/// delimiter-joined string, no two distinct rows can ever hash the same just because a value happens to
/// contain a character also used as a delimiter), and a table's fingerprint is the sum (ignoring
/// overflow) of its rows' hashes - deliberately order-independent, since a heap table has no guaranteed
/// row order, and ALTER COLUMN can itself reorganize a heap's physical row order even when no cell's
/// value actually changes.
///
/// A multi-table capture spans several independent SELECT statements, one per table - without a shared
/// point-in-time view, a concurrent write from another session in between two of them (the tool never
/// enforces exclusive database access) could be misreported as data the migration itself changed. When
/// the database has snapshot isolation enabled (ALLOW_SNAPSHOT_ISOLATION), the whole capture instead
/// runs inside one SNAPSHOT-isolated transaction, giving every table's SELECT the same consistent
/// point-in-time view without blocking (or being blocked by) other sessions; otherwise it falls back to
/// independent per-table reads rather than claiming a consistency guarantee it cannot deliver.
/// </summary>
public sealed class DataVerificationService(ISqlScriptRunnerFactory runnerFactory) : IDataVerificationService
{
    public async Task<IReadOnlyList<TableRowsSnapshot>> CaptureRowsAsync(
        ConnectionProfile profile, DatabaseSnapshot snapshot, IProgress<TableCaptureProgress>? progress = null, CancellationToken ct = default)
    {
        await using var runner = await runnerFactory.CreateAsync(profile, ct);
        var result = new List<TableRowsSnapshot>();
        var totalTables = snapshot.Tables.Count;

        var useSnapshotIsolation = await IsSnapshotIsolationEnabledAsync(runner, ct);
        if (useSnapshotIsolation)
            await runner.BeginTransactionAsync(IsolationLevel.Snapshot, ct);

        for (var i = 0; i < totalTables; i++)
        {
            var table = snapshot.Tables[i];

            // Reported before the query runs: a large table can dominate the phase's duration, so
            // reporting after would leave the UI showing no progress while it's being read.
            progress?.Report(new TableCaptureProgress(i + 1, totalTables, table.Ref.ToString()));

            var rowCount = 0;
            var fingerprint = 0UL;

            await foreach (var row in runner.ExecuteQueryStreamAsync($"SELECT * FROM {table.Ref.Identifier.Quoted};", ct: ct))
            {
                rowCount++;
                unchecked { fingerprint += HashRow(row); }
            }

            result.Add(new TableRowsSnapshot(table.Ref, rowCount, fingerprint));
        }

        if (useSnapshotIsolation)
            await runner.CommitAsync(ct);

        return result;
    }

    private static async Task<bool> IsSnapshotIsolationEnabledAsync(ISqlScriptRunner runner, CancellationToken ct)
    {
        var enabled = await runner.ExecuteScalarAsync<int>(
            "SELECT CASE WHEN snapshot_isolation_state = 1 THEN 1 ELSE 0 END FROM sys.databases WHERE database_id = DB_ID();", ct: ct);
        return enabled == 1;
    }

    public IReadOnlyList<DataDiff> Compare(IReadOnlyList<TableRowsSnapshot> before, IReadOnlyList<TableRowsSnapshot> after)
    {
        var diffs = new List<DataDiff>();
        var beforeByTable = before.ToDictionary(t => t.Table);
        var afterByTable = after.ToDictionary(t => t.Table);

        foreach (var tableRef in beforeByTable.Keys)
        {
            var description = tableRef.ToString();
            var beforeSnapshot = beforeByTable[tableRef];

            if (!afterByTable.TryGetValue(tableRef, out var afterSnapshot))
            {
                diffs.Add(new DataDiff(description, "Tabelle nach der Migration nicht mehr vorhanden."));
                continue;
            }

            if (beforeSnapshot.RowCount != afterSnapshot.RowCount)
            {
                diffs.Add(new DataDiff(description, $"Zeilenanzahl geändert: {beforeSnapshot.RowCount} -> {afterSnapshot.RowCount}."));
                continue;
            }

            if (beforeSnapshot.ContentFingerprint != afterSnapshot.ContentFingerprint)
                diffs.Add(new DataDiff(description, "Zeileninhalt weicht vom Vorher-Stand ab."));
        }

        return diffs;
    }

    private static ulong HashRow(IReadOnlyDictionary<string, object?> row)
    {
        using var buffer = new MemoryStream();
        foreach (var (key, value) in row.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            WriteLengthPrefixed(buffer, Encoding.UTF8.GetBytes(key));
            WriteLengthPrefixed(buffer, FormatValueBytes(value));
        }

        return BitConverter.ToUInt64(SHA256.HashData(buffer.ToArray()));
    }

    /// <summary>
    /// Every field is preceded by its own byte length, so no two distinct column/value pairs can ever
    /// serialize to the same byte sequence - a naive "Key=Value" string joined with a plain delimiter
    /// (no escaping) can otherwise let a value containing that same delimiter character make two
    /// genuinely different rows collide onto an identical string.
    /// </summary>
    private static void WriteLengthPrefixed(Stream stream, byte[] bytes)
    {
        stream.Write(BitConverter.GetBytes(bytes.Length));
        stream.Write(bytes);
    }

    /// <summary>
    /// Each type gets its own lossless, culture-invariant binary encoding rather than falling back to
    /// value.ToString() for everything - the default (culture-dependent, current-culture) formatting of
    /// a DateTime omits fractional seconds entirely (e.g. 12:00:00.100 and 12:00:00.900 both format to
    /// "12:00:00"), which would silently hide a real datetime2 value change as an identical fingerprint.
    /// </summary>
    private static byte[] FormatValueBytes(object? value) => value switch
    {
        null => [0],
        byte[] bytes => [1, .. bytes],
        DateTime dt => [2, .. BitConverter.GetBytes(dt.Ticks), .. BitConverter.GetBytes((int)dt.Kind)],
        DateTimeOffset dto => [3, .. BitConverter.GetBytes(dto.Ticks), .. BitConverter.GetBytes(dto.Offset.Ticks)],
        TimeSpan ts => [4, .. BitConverter.GetBytes(ts.Ticks)],
        decimal dec => [5, .. decimal.GetBits(dec).SelectMany(BitConverter.GetBytes)],
        double d => [6, .. BitConverter.GetBytes(d)],
        float f => [7, .. BitConverter.GetBytes(f)],
        Guid guid => [8, .. guid.ToByteArray()],
        _ => [9, .. Encoding.UTF8.GetBytes(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)]
    };
}
