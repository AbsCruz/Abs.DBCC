using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Verification;

/// <summary>
/// Captures a per-row hash for every row of every table, hashing values as read back through the data
/// reader - the right level to compare at, since a collation change can alter a string's underlying bytes
/// without changing its logical value.
///
/// Rows are sorted by hash, not content: a heap table has no guaranteed row order, and ALTER COLUMN can
/// itself reorder a heap's rows even when no value changes, which would otherwise read as a false diff.
/// Hashing also lets rows be streamed and discarded one at a time instead of held in memory, so
/// verification doesn't need a table's full content in memory twice (before + after).
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

            // Reported before the query runs: a large table can dominate the phase's duration, so
            // reporting after would leave the UI showing no progress while it's being read.
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
