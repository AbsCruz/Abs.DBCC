using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Verification;

/// <summary>
/// Captures every row of every table (as .NET values read back through the data reader - the correct
/// level to compare at, since a collation change can alter the underlying byte representation of a
/// string without changing its logical value) and compares two such captures.
///
/// Rows are compared after sorting both sides by a canonical string key rather than by relying on
/// database-returned order: a heap table (no primary key/clustered index) has no guaranteed row order,
/// and ALTER COLUMN can itself reorganize a heap's physical row order even when no cell's value
/// actually changes, which would otherwise look like a false difference.
/// </summary>
public sealed class DataVerificationService(ISqlScriptRunnerFactory runnerFactory) : IDataVerificationService
{
    public async Task<IReadOnlyList<TableRowsSnapshot>> CaptureRowsAsync(
        ConnectionProfile profile, DatabaseSnapshot snapshot, CancellationToken ct = default)
    {
        await using var runner = await runnerFactory.CreateAsync(profile, ct);
        var result = new List<TableRowsSnapshot>();

        foreach (var table in snapshot.Tables)
        {
            var rows = await runner.ExecuteQueryAsync($"SELECT * FROM {table.Ref.Identifier.Quoted};", ct: ct);
            result.Add(new TableRowsSnapshot(table.Ref, rows));
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

            var beforeRows = CanonicallyOrder(beforeByTable[tableRef].Rows);
            var afterRows = CanonicallyOrder(afterSnapshot.Rows);

            if (beforeRows.Count != afterRows.Count)
            {
                diffs.Add(new DataDiff(description, $"Zeilenanzahl geändert: {beforeRows.Count} -> {afterRows.Count}."));
                continue;
            }

            for (var i = 0; i < beforeRows.Count; i++)
            {
                if (!RowsEqual(beforeRows[i], afterRows[i]))
                    diffs.Add(new DataDiff(description, $"Zeile {i + 1} weicht vom Vorher-Stand ab."));
            }
        }

        return diffs;
    }

    private static List<IReadOnlyDictionary<string, object?>> CanonicallyOrder(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) =>
        rows.OrderBy(RowKey, StringComparer.Ordinal).ToList();

    private static string RowKey(IReadOnlyDictionary<string, object?> row) =>
        string.Join("|", row.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => $"{kv.Key}={FormatValue(kv.Value)}"));

    private static string FormatValue(object? value) => value switch
    {
        null => "<null>",
        byte[] bytes => Convert.ToHexString(bytes),
        _ => value.ToString() ?? "<null>"
    };

    private static bool RowsEqual(IReadOnlyDictionary<string, object?> before, IReadOnlyDictionary<string, object?> after)
    {
        if (before.Count != after.Count)
            return false;

        foreach (var (key, value) in before)
        {
            if (!after.TryGetValue(key, out var otherValue) || !ValuesEqual(value, otherValue))
                return false;
        }

        return true;
    }

    private static bool ValuesEqual(object? a, object? b) =>
        a is byte[] bytesA && b is byte[] bytesB
            ? bytesA.AsSpan().SequenceEqual(bytesB)
            : Equals(a, b);
}
