namespace Abs.DBCC.Domain.Snapshot;

/// <summary>
/// A computed column whose expression directly references a schema-bound view or function (e.g. a
/// computed column defined as "dbo.SomeFunction(...)"). SQL Server refuses to drop that view/function
/// while the computed column's expression still calls it - the same class of dependency as
/// <see cref="SchemaBoundObjectReference"/>, but with a computed column as the referencing side instead
/// of another schema-bound object.
/// </summary>
public sealed record ComputedColumnObjectReference(ObjectRef Table, string ColumnName, ObjectRef ReferencedObject);
