namespace Abs.DBCC.Domain.Inspection;

public sealed record TableCollationReport(
    string SchemaName,
    string TableName,
    IReadOnlyList<ColumnCollationState> Columns)
{
    /// <summary>True if the table's character columns are not all on the same collation.</summary>
    public bool IsMixedCollation =>
        Columns
            .Where(c => c.Collation is not null)
            .Select(c => c.Collation!.Value)
            .Distinct()
            .Count() > 1;
}
