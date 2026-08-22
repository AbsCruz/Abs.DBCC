using Abs.DBCC.Domain.Collation;

namespace Abs.DBCC.Domain.Snapshot;

public sealed record ColumnSnapshot(
    string Name,
    string SqlDataType,
    int? MaxLength,
    byte? Precision,
    byte? Scale,
    bool IsNullable,
    SqlCollationName? Collation,
    bool IsComputed,
    string? ComputedDefinition,
    bool IsComputedPersisted)
{
    /// <summary>True for a physical (non-computed) column that carries a collation, i.e. an ALTER COLUMN candidate.</summary>
    public bool IsCharacterType => !IsComputed && Collation is not null;
}
