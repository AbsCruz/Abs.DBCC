using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Verification;
using Abs.DBCC.TestCommon.Builders;

namespace Abs.DBCC.Infrastructure.Test.Verification;

public class DatabaseSnapshotComparerTests
{
    private static readonly SqlCollationName Target = new("Latin1_General_100_CI_AS_SC_UTF8");
    private static readonly SqlCollationName Source = new("SQL_Latin1_General_CP1_CI_AS");

    [Fact]
    public void Compare_IdenticalSnapshots_ProducesNoDiffs()
    {
        var table = new TableSnapshotBuilder("dbo", "Orders").WithColumn("Name", "varchar", Source.Value).Build();
        var snapshot = new DatabaseSnapshotBuilder().WithTable(table).Build();

        var diffs = DatabaseSnapshotComparer.Compare(snapshot, snapshot, Target);

        Assert.Empty(diffs);
    }

    [Fact]
    public void Compare_ExpectedCollationChangeToTarget_IsNotReported()
    {
        var before = new DatabaseSnapshotBuilder()
            .WithTable(new TableSnapshotBuilder("dbo", "Orders").WithColumn("Name", "varchar", Source.Value).Build())
            .Build();
        var after = new DatabaseSnapshotBuilder()
            .WithTable(new TableSnapshotBuilder("dbo", "Orders").WithColumn("Name", "varchar", Target.Value).Build())
            .Build();

        var diffs = DatabaseSnapshotComparer.Compare(before, after, Target);

        Assert.Empty(diffs);
    }

    [Fact]
    public void Compare_UnexpectedCollationChange_IsReported()
    {
        var before = new DatabaseSnapshotBuilder()
            .WithTable(new TableSnapshotBuilder("dbo", "Orders").WithColumn("Name", "varchar", Source.Value).Build())
            .Build();
        var after = new DatabaseSnapshotBuilder()
            .WithTable(new TableSnapshotBuilder("dbo", "Orders").WithColumn("Name", "varchar", "Latin1_General_CS_AS").Build())
            .Build();

        var diffs = DatabaseSnapshotComparer.Compare(before, after, Target);

        Assert.Single(diffs);
    }

    [Fact]
    public void Compare_MissingColumnAfterMigration_IsReported()
    {
        var before = new DatabaseSnapshotBuilder()
            .WithTable(new TableSnapshotBuilder("dbo", "Orders").WithColumn("Name", "varchar", Source.Value).WithColumn("Notes", "varchar", Source.Value).Build())
            .Build();
        var after = new DatabaseSnapshotBuilder()
            .WithTable(new TableSnapshotBuilder("dbo", "Orders").WithColumn("Name", "varchar", Target.Value).Build())
            .Build();

        var diffs = DatabaseSnapshotComparer.Compare(before, after, Target);

        Assert.Single(diffs);
        Assert.Contains("Notes", diffs[0].Details);
    }

    [Fact]
    public void Compare_MissingIndexAfterMigration_IsReported()
    {
        var before = new DatabaseSnapshotBuilder()
            .WithTable(new TableSnapshotBuilder("dbo", "Orders").WithColumn("Name", "varchar", Source.Value).WithIndex("IX_Orders_Name", ["Name"]).Build())
            .Build();
        var after = new DatabaseSnapshotBuilder()
            .WithTable(new TableSnapshotBuilder("dbo", "Orders").WithColumn("Name", "varchar", Target.Value).Build())
            .Build();

        var diffs = DatabaseSnapshotComparer.Compare(before, after, Target);

        Assert.Single(diffs);
        Assert.Contains("IX_Orders_Name", diffs[0].Details);
    }

    [Fact]
    public void Compare_ChangedForeignKeyDeleteAction_IsReported()
    {
        var orders = new TableSnapshotBuilder("dbo", "Orders").WithColumn("Id", "int", null).Build();
        var items = new TableSnapshotBuilder("dbo", "Items").WithColumn("OrderId", "int", null).Build();

        var before = new DatabaseSnapshotBuilder().WithTable(orders).WithTable(items)
            .WithForeignKey("FK_Items_Orders", items.Ref, orders.Ref, [("OrderId", "Id")], deleteAction: "NO_ACTION")
            .Build();
        var after = new DatabaseSnapshotBuilder().WithTable(orders).WithTable(items)
            .WithForeignKey("FK_Items_Orders", items.Ref, orders.Ref, [("OrderId", "Id")], deleteAction: "CASCADE")
            .Build();

        var diffs = DatabaseSnapshotComparer.Compare(before, after, Target);

        Assert.Single(diffs);
        Assert.Contains("FK_Items_Orders", diffs[0].Details);
    }

    [Fact]
    public void Compare_ViewDefinitionChanged_IsReported()
    {
        var before = new DatabaseSnapshotBuilder().WithView("dbo", "V1", "CREATE VIEW dbo.V1 AS SELECT 1;").Build();
        var after = new DatabaseSnapshotBuilder().WithView("dbo", "V1", "CREATE VIEW dbo.V1 AS SELECT 2;").Build();

        var diffs = DatabaseSnapshotComparer.Compare(before, after, Target);

        Assert.Single(diffs);
    }

    [Fact]
    public void Compare_MissingViewAfterMigration_IsReported()
    {
        var before = new DatabaseSnapshotBuilder().WithView("dbo", "V1", "CREATE VIEW dbo.V1 AS SELECT 1;").Build();
        var after = new DatabaseSnapshotBuilder().Build();

        var diffs = DatabaseSnapshotComparer.Compare(before, after, Target);

        Assert.Single(diffs);
    }

    [Fact]
    public void Compare_IdenticalViewDefinition_ProducesNoDiff()
    {
        var before = new DatabaseSnapshotBuilder().WithView("dbo", "V1", "CREATE VIEW dbo.V1 AS SELECT 1;", isSchemaBound: true).Build();
        var after = new DatabaseSnapshotBuilder().WithView("dbo", "V1", "CREATE VIEW dbo.V1 AS SELECT 1;", isSchemaBound: true).Build();

        var diffs = DatabaseSnapshotComparer.Compare(before, after, Target);

        Assert.Empty(diffs);
    }

    [Fact]
    public void Compare_ViewLastModifiedWithAlter_ReplayedAsCreateAfterMigration_ProducesNoDiff()
    {
        // Reproduces a real-world false positive: the migration replays a schema-bound object's
        // captured definition through RawDefinitionScriptGenerator, which rewrites a stale ALTER
        // header to CREATE before recreating it. A raw text comparison would then flag every such
        // object as "changed" even though nothing about it actually did - the comparer must replay
        // the "before" text the same way the migration does before comparing.
        var before = new DatabaseSnapshotBuilder().WithView("dbo", "V1", "ALTER VIEW dbo.V1 AS SELECT 1;", isSchemaBound: true).Build();
        var after = new DatabaseSnapshotBuilder().WithView("dbo", "V1", "CREATE VIEW [dbo].[V1] AS SELECT 1;", isSchemaBound: true).Build();

        var diffs = DatabaseSnapshotComparer.Compare(before, after, Target);

        Assert.Empty(diffs);
    }

    [Fact]
    public void Compare_ViewLastModifiedWithAlterAndBodyActuallyChanged_IsStillReported()
    {
        var before = new DatabaseSnapshotBuilder().WithView("dbo", "V1", "ALTER VIEW dbo.V1 AS SELECT 1;", isSchemaBound: true).Build();
        var after = new DatabaseSnapshotBuilder().WithView("dbo", "V1", "CREATE VIEW [dbo].[V1] AS SELECT 2;", isSchemaBound: true).Build();

        var diffs = DatabaseSnapshotComparer.Compare(before, after, Target);

        Assert.Single(diffs);
    }

    [Fact]
    public void Compare_MissingSequenceAfterMigration_IsReported()
    {
        var before = new DatabaseSnapshotBuilder().WithSequence("dbo", "Numbers").Build();
        var after = new DatabaseSnapshotBuilder().Build();

        var diffs = DatabaseSnapshotComparer.Compare(before, after, Target);

        Assert.Single(diffs);
    }

    [Fact]
    public void Compare_ChangedSynonymBaseObject_IsReported()
    {
        var before = new DatabaseSnapshotBuilder().WithSynonym("dbo", "S1", "dbo.Orders").Build();
        var after = new DatabaseSnapshotBuilder().WithSynonym("dbo", "S1", "dbo.OrdersV2").Build();

        var diffs = DatabaseSnapshotComparer.Compare(before, after, Target);

        Assert.Single(diffs);
    }

    [Fact]
    public void Compare_IdenticalFullTextSetup_ProducesNoDiff()
    {
        var table = new ObjectRef("dbo", "Articles", DatabaseObjectKind.Table);
        var before = new DatabaseSnapshotBuilder()
            .WithFullTextCatalog("Cat1")
            .WithFullTextIndex(table, "Cat1", "PK_Articles", ["Title"])
            .Build();
        var after = new DatabaseSnapshotBuilder()
            .WithFullTextCatalog("Cat1")
            .WithFullTextIndex(table, "Cat1", "PK_Articles", ["Title"])
            .Build();

        var diffs = DatabaseSnapshotComparer.Compare(before, after, Target);

        Assert.Empty(diffs);
    }

    [Fact]
    public void Compare_FullTextIndexChangeTrackingChanged_IsReported()
    {
        var table = new ObjectRef("dbo", "Articles", DatabaseObjectKind.Table);
        var before = new DatabaseSnapshotBuilder().WithFullTextIndex(table, "Cat1", "PK_Articles", ["Title"], "AUTO").Build();
        var after = new DatabaseSnapshotBuilder().WithFullTextIndex(table, "Cat1", "PK_Articles", ["Title"], "MANUAL").Build();

        var diffs = DatabaseSnapshotComparer.Compare(before, after, Target);

        Assert.Single(diffs);
    }

    [Fact]
    public void Compare_MissingPermissionAfterMigration_IsReported()
    {
        var table = new ObjectRef("dbo", "Orders", DatabaseObjectKind.Table);
        var before = new DatabaseSnapshotBuilder().WithObjectPermission("app_user", "SELECT", table).Build();
        var after = new DatabaseSnapshotBuilder().Build();

        var diffs = DatabaseSnapshotComparer.Compare(before, after, Target);

        Assert.Single(diffs);
    }

    [Fact]
    public void Compare_IdenticalPermissions_ProduceNoDiff()
    {
        var table = new ObjectRef("dbo", "Orders", DatabaseObjectKind.Table);
        var before = new DatabaseSnapshotBuilder().WithObjectPermission("app_user", "SELECT", table).WithDatabasePermission("app_user", "CONNECT").Build();
        var after = new DatabaseSnapshotBuilder().WithObjectPermission("app_user", "SELECT", table).WithDatabasePermission("app_user", "CONNECT").Build();

        var diffs = DatabaseSnapshotComparer.Compare(before, after, Target);

        Assert.Empty(diffs);
    }

    [Fact]
    public void Compare_ChangedExtendedPropertyValue_IsReported()
    {
        var table = new ObjectRef("dbo", "Orders", DatabaseObjectKind.Table);
        var before = new DatabaseSnapshotBuilder().WithExtendedProperty(table, null, "MS_Description", "old text").Build();
        var after = new DatabaseSnapshotBuilder().WithExtendedProperty(table, null, "MS_Description", "new text").Build();

        var diffs = DatabaseSnapshotComparer.Compare(before, after, Target);

        Assert.Single(diffs);
    }

    [Fact]
    public void Compare_MissingViewIndexAfterMigration_IsReported()
    {
        var view = new ObjectRef("dbo", "OrdersView", DatabaseObjectKind.View);
        var index = new IndexSnapshot("IX_OrdersView_Id", true, true, false, false, [new IndexColumnSnapshot("Id", false, false)], null);

        var before = new DatabaseSnapshotBuilder().WithViewIndex(view, index).Build();
        var after = new DatabaseSnapshotBuilder().Build();

        var diffs = DatabaseSnapshotComparer.Compare(before, after, Target);

        Assert.Single(diffs);
    }

    [Fact]
    public void Compare_IdenticalViewIndex_ProducesNoDiff()
    {
        var view = new ObjectRef("dbo", "OrdersView", DatabaseObjectKind.View);
        var index = new IndexSnapshot("IX_OrdersView_Id", true, true, false, false, [new IndexColumnSnapshot("Id", false, false)], null);

        var before = new DatabaseSnapshotBuilder().WithViewIndex(view, index).Build();
        var after = new DatabaseSnapshotBuilder().WithViewIndex(view, index).Build();

        var diffs = DatabaseSnapshotComparer.Compare(before, after, Target);

        Assert.Empty(diffs);
    }

    [Fact]
    public void Compare_MissingSchemaLevelPermissionAfterMigration_IsReported()
    {
        var before = new DatabaseSnapshotBuilder().WithSchemaPermission("app_user", "SELECT", "dbo").Build();
        var after = new DatabaseSnapshotBuilder().Build();

        var diffs = DatabaseSnapshotComparer.Compare(before, after, Target);

        Assert.Single(diffs);
    }

    [Fact]
    public void Compare_MissingTableAfterMigration_IsReported()
    {
        var before = new DatabaseSnapshotBuilder()
            .WithTable(new TableSnapshotBuilder("dbo", "Orders").WithColumn("Id", "int", null).Build())
            .Build();
        var after = new DatabaseSnapshotBuilder().Build();

        var diffs = DatabaseSnapshotComparer.Compare(before, after, Target);

        Assert.Single(diffs);
        Assert.Contains("fehlt", diffs[0].Details);
    }
}
