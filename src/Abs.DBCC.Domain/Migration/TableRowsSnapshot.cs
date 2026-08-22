using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Domain.Migration;

public sealed record TableRowsSnapshot(ObjectRef Table, IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);
