using Abs.DBCC.Domain.Collation;

namespace Abs.DBCC.Domain.Inspection;

public sealed record DatabaseCollationReport(
    SqlCollationName DatabaseDefaultCollation,
    IReadOnlyList<TableCollationReport> Tables);
