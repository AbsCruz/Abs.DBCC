namespace Abs.DBCC.Domain.Snapshot;

/// <summary>A synonym. Independent of any column's collation - never touched, captured for verification only.</summary>
public sealed record SynonymSnapshot(ObjectRef Ref, string BaseObjectName);
