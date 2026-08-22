using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Inspection;

namespace Abs.DBCC.Domain.Test;

public class TableCollationReportTests
{
    private static ColumnCollationState Column(string name, string? collation) =>
        new("dbo", "Orders", name, "nvarchar", IsCharacterType: collation is not null,
            collation is null ? null : new SqlCollationName(collation));

    [Fact]
    public void IsMixedCollation_False_WhenAllCharacterColumnsShareCollation()
    {
        var report = new TableCollationReport("dbo", "Orders",
        [
            Column("Id", null),
            Column("Name", "Latin1_General_CI_AS"),
            Column("Description", "Latin1_General_CI_AS")
        ]);

        Assert.False(report.IsMixedCollation);
    }

    [Fact]
    public void IsMixedCollation_True_WhenCharacterColumnsDiffer()
    {
        var report = new TableCollationReport("dbo", "Orders",
        [
            Column("Name", "Latin1_General_CI_AS"),
            Column("Description", "Latin1_General_100_CI_AS_SC_UTF8")
        ]);

        Assert.True(report.IsMixedCollation);
    }

    [Fact]
    public void IsMixedCollation_False_WhenNoCharacterColumns()
    {
        var report = new TableCollationReport("dbo", "Orders",
        [
            Column("Id", null),
            Column("Amount", null)
        ]);

        Assert.False(report.IsMixedCollation);
    }
}
