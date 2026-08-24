namespace Abs.DBCC.Domain.Snapshot;

/// <summary>
/// A WITH SCHEMABINDING view or function that directly references another view or function
/// (sys.sql_expression_dependencies) rather than a table column. This captures view-on-view (or
/// view-on-function) schema-bound chains, so a dependent like an indexed wrapper view built on top of
/// another schema-bound view can be ordered correctly around the object it references.
/// </summary>
public sealed record SchemaBoundObjectReference(ObjectRef DependentObject, ObjectRef ReferencedObject);
