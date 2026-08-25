namespace Abs.DBCC.Domain.Snapshot;

/// <summary>
/// A computed column whose expression calls a schema-bound view or function. SQL Server refuses to drop
/// that view/function while the expression still references it - the same dependency class as
/// <see cref="SchemaBoundObjectReference"/>, but with a computed column as the referencing side.
/// </summary>
public sealed record ComputedColumnObjectReference(ObjectRef Table, string ColumnName, ObjectRef ReferencedObject);
