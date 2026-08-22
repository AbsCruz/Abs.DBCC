namespace Abs.DBCC.Domain.Snapshot;

/// <summary>
/// A WITH SCHEMABINDING view or function that references a specific table column (sys.sql_expression_dependencies).
/// Only schema-bound objects are tracked here because only they block ALTER COLUMN on the referenced column -
/// plain views/procedures/functions/triggers merely resolve to the new collation implicitly, with no action needed.
/// </summary>
public sealed record SchemaBoundDependency(ObjectRef DependentObject, ObjectRef ReferencedTable, string ReferencedColumn);
