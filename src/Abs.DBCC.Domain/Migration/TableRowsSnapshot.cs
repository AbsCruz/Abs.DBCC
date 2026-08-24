using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Domain.Migration;

/// <summary>
/// A table's row count plus an order-independent content fingerprint (the sum, ignoring overflow, of
/// a strong per-row hash over every column) - not the rows themselves. Verifying a table with millions
/// of rows must not require holding all of them in memory twice (once for "before", once for "after");
/// this is deliberately small and fixed-size regardless of table size. The fingerprint being
/// order-independent also means it does not matter that a heap table's physical row order between the
/// "before" and "after" reads is not guaranteed to match.
/// </summary>
public sealed record TableRowsSnapshot(ObjectRef Table, int RowCount, ulong ContentFingerprint);
