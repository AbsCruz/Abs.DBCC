using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Verification;

/// <summary>
/// Captures a per-row hash for every row of every table (hashing the value as read back through the data
/// reader - the correct level to compare at, since a collation change can alter the underlying byte
/// representation of a string without changing its logical value) and compares two such captures.
///
/// Rows are hashed and sorted by hash rather than kept verbatim and sorted by content: a heap table (no
/// primary key/clustered index) has no guaranteed row order, and ALTER COLUMN can itself reorganize a
/// heap's physical row order even when no cell's value actually changes, which would otherwise look like
/// a false difference. Hashing also means a table's rows are streamed and discarded one at a time rather
/// than held in memory all at once, so verifying a large database doesn't require its full content to fit
/// in memory twice (before + after).
/// </summary>
public sealed class DataVerificationService(ISqlScriptRunnerFactory runnerFactory) : IDataVerificationService
{
    public async Task<IReadOnlyList<TableRowsSnapshot>> CaptureRowsAsync(
        ConnectionProfile profile, DatabaseSnapshot snapshot, IProgress<TableCaptureProgress>? progress = null, CancellationToken ct = default)
    {
        await using var runner = await runnerFactory.CreateAsync(profile, ct);
        var result = new List<TableRowsSnapshot>();
        var totalTables = snapshot.Tables.Count;

        for (var i = 0; i < totalTables; i++)
        {
            var table = snapshot.Tables[i];

            // Reported before the query runs, not after: a single large table can dominate the whole
            // phase's duration, and without this the UI would show no movement at all while it's being
            // read - the table name at least tells the user what it's currently waiting on.
            progress?.Report(new TableCaptureProgress(i + 1, totalTables, table.Ref.ToString()));

            var hashes = new List<string>();

            await foreach (var row in runner.ExecuteQueryStreamAsync($"SELECT * FROM {table.Ref.Identifier.Quoted};", ct: ct))
                hashes.Add(RowHash.Compute(row));

            hashes.Sort(StringComparer.Ordinal);
            result.Add(new TableRowsSnapshot(table.Ref, hashes));
        }

        return result;
    }

    public IReadOnlyList<DataDiff> Compare(IReadOnlyList<TableRowsSnapshot> before, IReadOnlyList<TableRowsSnapshot> after)
    {
        var diffs = new List<DataDiff>();
        var beforeByTable = before.ToDictionary(t => t.Table);
        var afterByTable = after.ToDictionary(t => t.Table);

        foreach (var tableRef in beforeByTable.Keys)
        {
            var description = tableRef.ToString();

            if (!afterByTable.TryGetValue(tableRef, out var afterSnapshot))
            {
                diffs.Add(new DataDiff(description, "Tabelle nach der Migration nicht mehr vorhanden."));
                continue;
            }

            var beforeHashes = beforeByTable[tableRef].RowHashes;
            var afterHashes = afterSnapshot.RowHashes;

            if (beforeHashes.Count != afterHashes.Count)
            {
                diffs.Add(new DataDiff(description, $"Zeilenanzahl geändert: {beforeHashes.Count} -> {afterHashes.Count}."));
                continue;
            }

            for (var i = 0; i < beforeHashes.Count; i++)
            {
                if (beforeHashes[i] != afterHashes[i])
                    diffs.Add(new DataDiff(description, $"Zeile {i + 1} weicht vom Vorher-Stand ab."));
            }
        }

        return diffs;
    }
}
