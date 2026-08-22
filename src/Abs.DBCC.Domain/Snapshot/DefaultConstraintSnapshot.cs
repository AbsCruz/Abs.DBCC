namespace Abs.DBCC.Domain.Snapshot;

public sealed record DefaultConstraintSnapshot(string Name, string ColumnName, string Definition);
