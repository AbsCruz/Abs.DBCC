namespace Abs.DBCC.Domain.Snapshot;

/// <summary>
/// ColumnName is set when SQL Server attributes the constraint to a single column
/// (sys.check_constraints.parent_column_id); null for table-level, multi-column expressions.
/// </summary>
public sealed record CheckConstraintSnapshot(string Name, string? ColumnName, string Definition);
