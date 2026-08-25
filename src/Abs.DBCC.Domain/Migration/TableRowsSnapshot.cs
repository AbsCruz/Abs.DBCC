using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Domain.Migration;

/// <summary>
/// Per-table row hashes captured at one point in time, sorted canonically so a heap table's undefined
/// physical row order doesn't produce false diffs. Rows are hashed rather than kept verbatim so verifying
/// a large database doesn't require holding its full content in memory.
/// </summary>
public sealed record TableRowsSnapshot(ObjectRef Table, IReadOnlyList<string> RowHashes);
