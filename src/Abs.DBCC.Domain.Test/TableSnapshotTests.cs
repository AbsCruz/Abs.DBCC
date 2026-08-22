using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Domain.Test;

public class TableSnapshotTests
{
    private static ColumnSnapshot Column(string name, string? collation, bool isComputed = false) =>
        new(name, "nvarchar", 100, null, null, true,
            collation is null ? null : new SqlCollationName(collation),
            isComputed, null, false);

    private static TableSnapshot Table(params ColumnSnapshot[] columns) =>
        new(new ObjectRef("dbo", "Orders", DatabaseObjectKind.Table), columns, [], [], []);

    [Fact]
    public void ColumnsRequiringCollationChange_ExcludesColumnsAlreadyOnTarget()
    {
        var target = new SqlCollationName("Latin1_General_100_CI_AS_SC_UTF8");
        var table = Table(
            Column("Name", "SQL_Latin1_General_CP1_CI_AS"),
            Column("Description", "Latin1_General_100_CI_AS_SC_UTF8"),
            Column("Id", null));

        var result = table.ColumnsRequiringCollationChange(target).ToList();

        Assert.Single(result);
        Assert.Equal("Name", result[0].Name);
    }

    [Fact]
    public void ColumnsRequiringCollationChange_ExcludesComputedColumns()
    {
        var target = new SqlCollationName("Latin1_General_100_CI_AS_SC_UTF8");
        var table = Table(Column("Computed", "SQL_Latin1_General_CP1_CI_AS", isComputed: true));

        var result = table.ColumnsRequiringCollationChange(target).ToList();

        Assert.Empty(result);
    }
}

public class IndexSnapshotTests
{
    [Fact]
    public void CoversColumn_IsCaseInsensitive()
    {
        var index = new IndexSnapshot(
            "IX_Orders_Name", IsUnique: false, IsClustered: false, IsPrimaryKey: false, IsUniqueConstraint: false,
            [new IndexColumnSnapshot("Name", false, false)], null);

        Assert.True(index.CoversColumn("name"));
        Assert.False(index.CoversColumn("Other"));
    }

    [Fact]
    public void IsTableConstraint_TrueForPrimaryKeyOrUniqueConstraint()
    {
        var pk = new IndexSnapshot("PK_Orders", true, true, true, false, [], null);
        var plain = new IndexSnapshot("IX_Orders_Name", false, false, false, false, [], null);

        Assert.True(pk.IsTableConstraint);
        Assert.False(plain.IsTableConstraint);
    }
}

public class ForeignKeySnapshotTests
{
    [Fact]
    public void ReferencesParentColumn_And_ReferencesReferencedColumn_MatchCaseInsensitively()
    {
        var fk = new ForeignKeySnapshot(
            "FK_Orders_Customers",
            new ObjectRef("dbo", "Orders", DatabaseObjectKind.Table),
            new ObjectRef("dbo", "Customers", DatabaseObjectKind.Table),
            [new ForeignKeyColumnSnapshot("CustomerId", "Id")],
            "NO_ACTION", "NO_ACTION", false);

        Assert.True(fk.ReferencesParentColumn("customerid"));
        Assert.True(fk.ReferencesReferencedColumn("ID"));
        Assert.False(fk.ReferencesParentColumn("Other"));
    }
}
