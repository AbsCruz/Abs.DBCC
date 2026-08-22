using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Migration;
using Abs.DBCC.TestCommon.Builders;

namespace Abs.DBCC.Infrastructure.Test.Migration;

public class MigrationPlanBuilderTests
{
    private static readonly SqlCollationName Target = new("Latin1_General_100_CI_AS_SC_UTF8");
    private static readonly SqlCollationName Source = new("SQL_Latin1_General_CP1_CI_AS");
    private readonly MigrationPlanBuilder _sut = new();

    [Fact]
    public void Build_ColumnWithNoDependents_OnlyEmitsAlterColumnStep()
    {
        var table = new TableSnapshotBuilder("dbo", "Orders")
            .WithColumn("Name", "varchar", Source.Value)
            .Build();
        var snapshot = new DatabaseSnapshotBuilder().WithDatabaseCollation(Source.Value).WithTable(table).Build();

        var plan = _sut.Build(snapshot, Target, updateDatabaseDefaultCollation: false);

        var step = Assert.Single(plan.Steps);
        Assert.Equal(MigrationStepKind.AlterColumnCollation, step.Kind);
    }

    [Fact]
    public void Build_ColumnAlreadyOnTargetCollation_IsIgnored()
    {
        var table = new TableSnapshotBuilder("dbo", "Orders")
            .WithColumn("Name", "varchar", Target.Value)
            .Build();
        var snapshot = new DatabaseSnapshotBuilder().WithTable(table).Build();

        var plan = _sut.Build(snapshot, Target, updateDatabaseDefaultCollation: false);

        Assert.Empty(plan.Steps);
    }

    [Fact]
    public void Build_UpdateDatabaseDefaultCollation_RunsRightAfterTheLastAlterColumnStep()
    {
        // With no dependent objects to recreate, ALTER DATABASE is also the last step in this scenario -
        // but the invariant under test is "immediately after the alters", not "always last" (see the
        // full-dependency-scenario test below for a case where recreate steps follow it).
        var table = new TableSnapshotBuilder("dbo", "Orders").WithColumn("Name", "varchar", Source.Value).Build();
        var snapshot = new DatabaseSnapshotBuilder().WithTable(table).Build();

        var plan = _sut.Build(snapshot, Target, updateDatabaseDefaultCollation: true);
        var steps = plan.Steps.ToList();

        var lastAlterColumn = steps.FindLastIndex(s => s.Kind == MigrationStepKind.AlterColumnCollation);
        var dbCollationIndex = steps.FindIndex(s => s.Kind == MigrationStepKind.AlterDatabaseCollation);

        Assert.Equal(lastAlterColumn + 1, dbCollationIndex);
        Assert.Contains(Target.Value, steps[dbCollationIndex].Sql);
    }

    [Fact]
    public void Build_FullDependencyScenario_DropsAndRecreatesEverythingInSafeOrder()
    {
        var orders = new TableSnapshotBuilder("dbo", "Orders")
            .WithColumn("Id", "int", null, isNullable: false)
            .WithColumn("CustomerName", "varchar", Source.Value, maxLength: 100)
            .WithComputedColumn("DisplayName", "[CustomerName]+'!'")
            .WithIndex("PK_Orders", ["Id"], isUnique: true, isClustered: true, isPrimaryKey: true)
            .WithIndex("IX_Orders_CustomerName", ["CustomerName"])
            .WithCheckConstraint("CK_Orders_CustomerName", "CustomerName", "([CustomerName]<>'')")
            .WithCheckConstraint("CK_Orders_MultiCol", null, "([CustomerName] IS NOT NULL)")
            .WithCheckConstraint("CK_Orders_Unrelated", null, "([Id]>(0))")
            .WithDefaultConstraint("DF_Orders_CustomerName", "CustomerName", "('unknown')")
            .Build();

        // OrderCustomerName is already on the target collation - it must stay untouched by ALTER COLUMN;
        // it exists purely to prove the FK gets dropped because it references Orders.CustomerName, which changes.
        var notes = new TableSnapshotBuilder("dbo", "Notes")
            .WithColumn("Id", "int", null, isNullable: false)
            .WithColumn("OrderCustomerName", "varchar", Target.Value, maxLength: 100)
            .Build();

        var snapshot = new DatabaseSnapshotBuilder()
            .WithTable(orders)
            .WithTable(notes)
            .WithForeignKey("FK_Notes_Orders", notes.Ref, orders.Ref, [("OrderCustomerName", "CustomerName")])
            .Build();

        var plan = _sut.Build(snapshot, Target, updateDatabaseDefaultCollation: true);
        var steps = plan.Steps;

        // Everything that blocks ALTER COLUMN must be dropped.
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropForeignKey && s.Sql.Contains("FK_Notes_Orders"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropIndex && s.Sql.Contains("IX_Orders_CustomerName"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropCheckConstraint && s.Sql.Contains("CK_Orders_CustomerName"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropCheckConstraint && s.Sql.Contains("CK_Orders_MultiCol"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropDefaultConstraint && s.Sql.Contains("DF_Orders_CustomerName"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropComputedColumn && s.Sql.Contains("DisplayName"));

        // The unrelated check constraint and the PK (on a column that isn't changing) must survive untouched.
        Assert.DoesNotContain(steps, s => s.Sql.Contains("CK_Orders_Unrelated"));
        Assert.DoesNotContain(steps, s => s.Kind == MigrationStepKind.DropIndex && s.Sql.Contains("PK_Orders"));

        // Exactly one alter-column step, for CustomerName only.
        var alterSteps = steps.Where(s => s.Kind == MigrationStepKind.AlterColumnCollation).ToList();
        var alterStep = Assert.Single(alterSteps);
        Assert.Contains("CustomerName", alterStep.Sql);

        // Everything dropped must be recreated.
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.AddComputedColumn && s.Sql.Contains("DisplayName"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.AddDefaultConstraint && s.Sql.Contains("DF_Orders_CustomerName"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.AddCheckConstraint && s.Sql.Contains("CK_Orders_CustomerName"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.CreateIndex && s.Sql.Contains("IX_Orders_CustomerName"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.AddForeignKey && s.Sql.Contains("FK_Notes_Orders"));

        // Ordering invariants that make the plan actually executable against SQL Server.
        int IndexOfFirst(Func<MigrationStep, bool> predicate) => steps.ToList().FindIndex(s => predicate(s));

        Assert.True(IndexOfFirst(s => s.Kind == MigrationStepKind.DropForeignKey) < IndexOfFirst(s => s.Kind == MigrationStepKind.DropIndex));
        Assert.True(IndexOfFirst(s => s.Kind == MigrationStepKind.DropIndex) < IndexOfFirst(s => s.Kind == MigrationStepKind.AlterColumnCollation));
        // ALTER DATABASE must run before any recreate step (it would otherwise be blocked by the very
        // objects those steps are about to bring back) but after every ALTER COLUMN.
        Assert.True(IndexOfFirst(s => s.Kind == MigrationStepKind.AlterColumnCollation) < IndexOfFirst(s => s.Kind == MigrationStepKind.AlterDatabaseCollation));
        Assert.True(IndexOfFirst(s => s.Kind == MigrationStepKind.AlterDatabaseCollation) < IndexOfFirst(s => s.Kind == MigrationStepKind.AddComputedColumn));
        Assert.True(IndexOfFirst(s => s.Kind == MigrationStepKind.CreateIndex) < IndexOfFirst(s => s.Kind == MigrationStepKind.AddForeignKey));
    }

    [Fact]
    public void Build_ForeignKeyOnNonChangingColumns_IsNotTouched()
    {
        var orders = new TableSnapshotBuilder("dbo", "Orders")
            .WithColumn("Id", "int", null, isNullable: false)
            .WithColumn("Name", "varchar", Source.Value)
            .Build();
        var items = new TableSnapshotBuilder("dbo", "OrderItems")
            .WithColumn("OrderId", "int", null, isNullable: false)
            .Build();

        var snapshot = new DatabaseSnapshotBuilder()
            .WithTable(orders)
            .WithTable(items)
            .WithForeignKey("FK_Items_Orders", items.Ref, orders.Ref, [("OrderId", "Id")])
            .Build();

        var plan = _sut.Build(snapshot, Target, updateDatabaseDefaultCollation: false);

        Assert.DoesNotContain(plan.Steps, s => s.Kind is MigrationStepKind.DropForeignKey or MigrationStepKind.AddForeignKey);
    }

    [Fact]
    public void Build_SchemaBoundViewDependingOnChangingColumn_IsDroppedAndRecreated()
    {
        var orders = new TableSnapshotBuilder("dbo", "Orders")
            .WithColumn("Id", "int", null, isNullable: false)
            .WithColumn("CustomerName", "varchar", Source.Value)
            .Build();

        const string viewDefinition = "CREATE VIEW [dbo].[OrdersView] WITH SCHEMABINDING AS SELECT Id, CustomerName FROM dbo.Orders;";
        var snapshot = new DatabaseSnapshotBuilder()
            .WithTable(orders)
            .WithView("dbo", "OrdersView", viewDefinition, isSchemaBound: true)
            .WithSchemaBoundDependency(
                new ObjectRef("dbo", "OrdersView", DatabaseObjectKind.View), orders.Ref, "CustomerName")
            .Build();

        var plan = _sut.Build(snapshot, Target, updateDatabaseDefaultCollation: false);
        var steps = plan.Steps;

        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropSchemaBoundObject && s.Sql.Contains("OrdersView"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.AddSchemaBoundObject && s.Sql == viewDefinition);

        var dropIndex = steps.ToList().FindIndex(s => s.Kind == MigrationStepKind.DropSchemaBoundObject);
        var alterIndex = steps.ToList().FindIndex(s => s.Kind == MigrationStepKind.AlterColumnCollation);
        var addIndex = steps.ToList().FindIndex(s => s.Kind == MigrationStepKind.AddSchemaBoundObject);
        Assert.True(dropIndex < alterIndex);
        Assert.True(alterIndex < addIndex);
    }

    [Fact]
    public void Build_SchemaBoundViewOnUnrelatedColumn_IsNotTouched()
    {
        var orders = new TableSnapshotBuilder("dbo", "Orders")
            .WithColumn("Id", "int", null, isNullable: false)
            .WithColumn("CustomerName", "varchar", Source.Value)
            .Build();

        var snapshot = new DatabaseSnapshotBuilder()
            .WithTable(orders)
            .WithView("dbo", "OrdersByIdView", "CREATE VIEW [dbo].[OrdersByIdView] WITH SCHEMABINDING AS SELECT Id FROM dbo.Orders;", isSchemaBound: true)
            .WithSchemaBoundDependency(new ObjectRef("dbo", "OrdersByIdView", DatabaseObjectKind.View), orders.Ref, "Id")
            .Build();

        var plan = _sut.Build(snapshot, Target, updateDatabaseDefaultCollation: false);

        Assert.DoesNotContain(plan.Steps, s => s.Kind is MigrationStepKind.DropSchemaBoundObject or MigrationStepKind.AddSchemaBoundObject);
    }

    [Fact]
    public void Build_IndexedViewDependingOnChangingColumn_DropsViewIndexBeforeViewAndRecreatesAfter()
    {
        var orders = new TableSnapshotBuilder("dbo", "Orders")
            .WithColumn("Id", "int", null, isNullable: false)
            .WithColumn("CustomerName", "varchar", Source.Value)
            .Build();

        var viewRef = new ObjectRef("dbo", "OrdersView", DatabaseObjectKind.View);
        var viewIndex = new IndexSnapshot(
            "IX_OrdersView_Id", true, true, false, false, [new IndexColumnSnapshot("Id", false, false)], null);

        var snapshot = new DatabaseSnapshotBuilder()
            .WithTable(orders)
            .WithView("dbo", "OrdersView", "CREATE VIEW [dbo].[OrdersView] WITH SCHEMABINDING AS SELECT Id, CustomerName FROM dbo.Orders;", isSchemaBound: true)
            .WithSchemaBoundDependency(viewRef, orders.Ref, "CustomerName")
            .WithViewIndex(viewRef, viewIndex)
            .Build();

        var plan = _sut.Build(snapshot, Target, updateDatabaseDefaultCollation: false);
        var steps = plan.Steps.ToList();

        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropIndex && s.Sql.Contains("IX_OrdersView_Id") && s.Sql.Contains("OrdersView"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.CreateIndex && s.Sql.Contains("IX_OrdersView_Id") && s.Sql.Contains("OrdersView"));

        var dropIndex = steps.FindIndex(s => s.Kind == MigrationStepKind.DropIndex);
        var dropView = steps.FindIndex(s => s.Kind == MigrationStepKind.DropSchemaBoundObject);
        var addView = steps.FindIndex(s => s.Kind == MigrationStepKind.AddSchemaBoundObject);
        var addIndex = steps.FindIndex(s => s.Kind == MigrationStepKind.CreateIndex);

        Assert.True(dropIndex < dropView, "the view's own index must be dropped before DROP VIEW is attempted");
        Assert.True(addView < addIndex, "the index can only be recreated once the view exists again");
    }

    [Fact]
    public void Build_FullTextIndexCoveringChangingColumn_IsDroppedBeforeIndexesAndRecreatedAfter()
    {
        var articles = new TableSnapshotBuilder("dbo", "Articles")
            .WithColumn("Id", "int", null, isNullable: false)
            .WithColumn("Title", "nvarchar", Source.Value, maxLength: 200)
            .WithIndex("PK_Articles", ["Id"], isUnique: true, isClustered: true, isPrimaryKey: true)
            .Build();

        var snapshot = new DatabaseSnapshotBuilder()
            .WithTable(articles)
            .WithFullTextCatalog("MainCatalog", isDefault: true)
            .WithFullTextIndex(articles.Ref, "MainCatalog", "PK_Articles", ["Title"])
            .Build();

        var plan = _sut.Build(snapshot, Target, updateDatabaseDefaultCollation: false);
        var steps = plan.Steps.ToList();

        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropFullTextIndex && s.Sql.Contains("Articles"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.AddFullTextIndex && s.Sql.Contains("PK_Articles"));

        // PK_Articles (on Id, unrelated to the changing Title column) must never be touched - the
        // full-text index on Title must be dropped and recreated entirely on its own around the alter.
        Assert.DoesNotContain(steps, s => s.Kind is MigrationStepKind.DropIndex or MigrationStepKind.CreateIndex);

        var dropFullText = steps.FindIndex(s => s.Kind == MigrationStepKind.DropFullTextIndex);
        var alterColumn = steps.FindIndex(s => s.Kind == MigrationStepKind.AlterColumnCollation);
        var addFullText = steps.FindIndex(s => s.Kind == MigrationStepKind.AddFullTextIndex);

        Assert.True(dropFullText < alterColumn);
        Assert.True(alterColumn < addFullText);
    }

    [Fact]
    public void Build_SequencesAndSynonyms_AreNeverTouched()
    {
        var snapshot = new DatabaseSnapshotBuilder()
            .WithTable(new TableSnapshotBuilder("dbo", "Orders").WithColumn("Name", "varchar", Source.Value).Build())
            .WithSequence("dbo", "OrderNumbers")
            .WithSynonym("dbo", "OrdersSyn", "OtherDb.dbo.Orders")
            .Build();

        var plan = _sut.Build(snapshot, Target, updateDatabaseDefaultCollation: false);

        Assert.Single(plan.Steps); // only the AlterColumnCollation step for Orders.Name
    }

    [Fact]
    public void Build_ExtendedPropertyOnDroppedComputedColumn_IsReapplied()
    {
        var orders = new TableSnapshotBuilder("dbo", "Orders")
            .WithColumn("Id", "int", null, isNullable: false)
            .WithColumn("CustomerName", "varchar", Source.Value)
            .WithComputedColumn("DisplayName", "[CustomerName]+'!'")
            .Build();

        var snapshot = new DatabaseSnapshotBuilder()
            .WithTable(orders)
            .WithExtendedProperty(orders.Ref, "DisplayName", "MS_Description", "friendly label")
            .WithExtendedProperty(orders.Ref, "Id", "MS_Description", "primary key - must survive untouched")
            .Build();

        var plan = _sut.Build(snapshot, Target, updateDatabaseDefaultCollation: false);
        var steps = plan.Steps.ToList();

        var addExtProp = steps.Single(s => s.Kind == MigrationStepKind.AddExtendedProperty);
        Assert.Contains("friendly label", addExtProp.Sql);
        Assert.DoesNotContain(steps, s => s.Sql.Contains("primary key - must survive untouched"));

        var addComputed = steps.FindIndex(s => s.Kind == MigrationStepKind.AddComputedColumn);
        var addProp = steps.FindIndex(s => s.Kind == MigrationStepKind.AddExtendedProperty);
        Assert.True(addComputed < addProp, "extended property must be reapplied after the column exists again");
    }

    [Fact]
    public void Build_PermissionAndExtendedPropertyOnDroppedSchemaBoundView_AreReapplied()
    {
        var orders = new TableSnapshotBuilder("dbo", "Orders")
            .WithColumn("Id", "int", null, isNullable: false)
            .WithColumn("CustomerName", "varchar", Source.Value)
            .Build();

        var viewRef = new ObjectRef("dbo", "OrdersView", DatabaseObjectKind.View);
        var snapshot = new DatabaseSnapshotBuilder()
            .WithTable(orders)
            .WithView("dbo", "OrdersView", "CREATE VIEW [dbo].[OrdersView] WITH SCHEMABINDING AS SELECT Id, CustomerName FROM dbo.Orders;", isSchemaBound: true)
            .WithSchemaBoundDependency(viewRef, orders.Ref, "CustomerName")
            .WithObjectPermission("reporting_role", "SELECT", viewRef)
            .WithExtendedProperty(viewRef, null, "MS_Description", "Customer-facing orders view")
            .Build();

        var plan = _sut.Build(snapshot, Target, updateDatabaseDefaultCollation: false);
        var steps = plan.Steps.ToList();

        Assert.Contains(steps, s => s.Kind == MigrationStepKind.GrantPermission && s.Sql.Contains("reporting_role"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.AddExtendedProperty && s.Sql.Contains("Customer-facing orders view"));

        var addView = steps.FindIndex(s => s.Kind == MigrationStepKind.AddSchemaBoundObject);
        var grant = steps.FindIndex(s => s.Kind == MigrationStepKind.GrantPermission);
        var addProp = steps.FindIndex(s => s.Kind == MigrationStepKind.AddExtendedProperty && s.Sql.Contains("Customer-facing"));
        Assert.True(addView < grant);
        Assert.True(addView < addProp);
    }

    [Fact]
    public void Build_ExtendedPropertyOnDroppedCheckConstraint_IsReapplied()
    {
        var orders = new TableSnapshotBuilder("dbo", "Orders")
            .WithColumn("CustomerName", "varchar", Source.Value)
            .WithCheckConstraint("CK_Orders_CustomerName", "CustomerName", "([CustomerName]<>'')")
            .Build();

        var snapshot = new DatabaseSnapshotBuilder()
            .WithTable(orders)
            .WithExtendedProperty(
                new ObjectRef("dbo", "CK_Orders_CustomerName", DatabaseObjectKind.CheckConstraint), null,
                "MS_Description", "must not be blank", parentTable: orders.Ref)
            .Build();

        var plan = _sut.Build(snapshot, Target, updateDatabaseDefaultCollation: false);
        var steps = plan.Steps.ToList();

        var addExtProp = steps.Single(s => s.Kind == MigrationStepKind.AddExtendedProperty);
        Assert.Contains("must not be blank", addExtProp.Sql);

        var addCheck = steps.FindIndex(s => s.Kind == MigrationStepKind.AddCheckConstraint);
        var addProp = steps.FindIndex(s => s.Kind == MigrationStepKind.AddExtendedProperty);
        Assert.True(addCheck < addProp);
    }

    [Fact]
    public void Build_ExtendedPropertyOnDroppedIndex_IsReapplied()
    {
        var orders = new TableSnapshotBuilder("dbo", "Orders")
            .WithColumn("CustomerName", "varchar", Source.Value)
            .WithIndex("IX_Orders_CustomerName", ["CustomerName"])
            .Build();

        var snapshot = new DatabaseSnapshotBuilder()
            .WithTable(orders)
            .WithExtendedProperty(
                new ObjectRef("dbo", "IX_Orders_CustomerName", DatabaseObjectKind.Index), null,
                "MS_Description", "speeds up lookups", parentTable: orders.Ref)
            .Build();

        var plan = _sut.Build(snapshot, Target, updateDatabaseDefaultCollation: false);
        var steps = plan.Steps.ToList();

        var addExtProp = steps.Single(s => s.Kind == MigrationStepKind.AddExtendedProperty);
        Assert.Contains("speeds up lookups", addExtProp.Sql);

        var addIndex = steps.FindIndex(s => s.Kind == MigrationStepKind.CreateIndex);
        var addProp = steps.FindIndex(s => s.Kind == MigrationStepKind.AddExtendedProperty);
        Assert.True(addIndex < addProp);
    }

    [Fact]
    public void Build_PreSnapshot_IsTheOriginalSnapshot()
    {
        var snapshot = new DatabaseSnapshotBuilder().Build();

        var plan = _sut.Build(snapshot, Target, updateDatabaseDefaultCollation: false);

        Assert.Same(snapshot, plan.PreSnapshot);
        Assert.Equal(snapshot.DatabaseCollation, plan.SourceCollation);
        Assert.Equal(Target, plan.TargetCollation);
    }
}
