namespace Abs.DBCC.Domain.Snapshot;

/// <summary>
/// A sequence object. Independent of any column's collation - never touched by a migration, captured
/// purely so structural verification can prove it survived the migration unchanged.
/// </summary>
public sealed record SequenceSnapshot(
    ObjectRef Ref,
    string DataType,
    string StartValue,
    string Increment,
    string? MinValue,
    string? MaxValue,
    bool IsCycling,
    long CacheSize);
