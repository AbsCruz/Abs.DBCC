namespace Abs.DBCC.Domain.Snapshot;

/// <summary>An index defined directly on a schema-bound (indexed) view - structurally identical to a table index, just scoped to a view.</summary>
public sealed record ViewIndexSnapshot(ObjectRef View, IndexSnapshot Index);
