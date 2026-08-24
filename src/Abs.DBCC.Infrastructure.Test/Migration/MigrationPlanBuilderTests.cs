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

        // updateDatabaseDefaultCollation is true here, so even a check constraint unrelated to any
        // changing column must be dropped and recreated too - see
        // Build_UpdateDatabaseDefaultCollation_SweepsEveryCheckConstraintComputedColumnAndFilteredIndex
        // for the dedicated test. The PK (a non-filtered index on a column that isn't changing) is
        // untouched regardless, since plain indexes never depend on collation.
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropCheckConstraint && s.Sql.Contains("CK_Orders_Unrelated"));
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
    public void Build_UpdateDatabaseDefaultCollation_SweepsEveryCheckConstraintComputedColumnAndFilteredIndex()
    {
        // Reproduces a real-world failure: ALTER DATABASE ... COLLATE checks dependencies across the
        // WHOLE database, not just the columns being altered - a check constraint, computed column,
        // filtered index or schema-bound view on a table that has nothing to do with the migrating
        // column can still block it (SQL Server reports these by name, e.g. "Object 'X' depends on
        // database collation"). There is no catalog view that predicts this in advance, so when this
        // flag is set, every one of these across the database must be dropped and recreated regardless
        // of which column(s) they touch.
        var orders = new TableSnapshotBuilder("dbo", "Orders")
            .WithColumn("Name", "varchar", Source.Value)
            .Build();

        var unrelated = new TableSnapshotBuilder("dbo", "Unrelated")
            .WithColumn("Id", "int", null, isNullable: false)
            .WithColumn("Status", "varchar", Target.Value, maxLength: 20)
            .WithComputedColumn("StatusLabel", "[Status]+'!'")
            .WithIndex("Idx_Unrelated_Id_Where_IdIsNotNull", ["Id"], filter: "([Id] IS NOT NULL)")
            .WithIndex("IX_Unrelated_Plain", ["Id"])
            .WithCheckConstraint("Ck_Unrelated_Status", "Status", "([Status]<>'')")
            .Build();

        var unrelatedViewRef = new ObjectRef("dbo", "vUnrelated", DatabaseObjectKind.View);
        var snapshot = new DatabaseSnapshotBuilder()
            .WithTable(orders)
            .WithTable(unrelated)
            .WithView("dbo", "vUnrelated", "CREATE VIEW [dbo].[vUnrelated] WITH SCHEMABINDING AS SELECT Id FROM dbo.Unrelated;", isSchemaBound: true)
            .Build();

        var plan = _sut.Build(snapshot, Target, updateDatabaseDefaultCollation: true);
        var steps = plan.Steps;

        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropCheckConstraint && s.Sql.Contains("Ck_Unrelated_Status"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropComputedColumn && s.Sql.Contains("StatusLabel"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropIndex && s.Sql.Contains("Idx_Unrelated_Id_Where_IdIsNotNull"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropSchemaBoundObject && s.Sql.Contains("vUnrelated"));

        // A plain (non-filtered) index never depends on collation and must be left alone.
        Assert.DoesNotContain(steps, s => s.Sql.Contains("IX_Unrelated_Plain"));

        Assert.Contains(steps, s => s.Kind == MigrationStepKind.AddCheckConstraint && s.Sql.Contains("Ck_Unrelated_Status"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.AddComputedColumn && s.Sql.Contains("StatusLabel"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.CreateIndex && s.Sql.Contains("Idx_Unrelated_Id_Where_IdIsNotNull"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.AddSchemaBoundObject && s.Sql.Contains("vUnrelated"));
    }

    [Fact]
    public void Build_UpdateDatabaseDefaultCollationFalse_LeavesUnrelatedObjectsCompletelyUntouched()
    {
        var orders = new TableSnapshotBuilder("dbo", "Orders")
            .WithColumn("Name", "varchar", Source.Value)
            .Build();

        var unrelated = new TableSnapshotBuilder("dbo", "Unrelated")
            .WithColumn("Id", "int", null, isNullable: false)
            .WithColumn("Status", "varchar", Target.Value, maxLength: 20)
            .WithComputedColumn("StatusLabel", "[Status]+'!'")
            .WithIndex("Idx_Unrelated_Id_Where_IdIsNotNull", ["Id"], filter: "([Id] IS NOT NULL)")
            .WithCheckConstraint("Ck_Unrelated_Status", "Status", "([Status]<>'')")
            .Build();

        var snapshot = new DatabaseSnapshotBuilder()
            .WithTable(orders)
            .WithTable(unrelated)
            .WithView("dbo", "vUnrelated", "CREATE VIEW [dbo].[vUnrelated] WITH SCHEMABINDING AS SELECT Id FROM dbo.Unrelated;", isSchemaBound: true)
            .Build();

        var plan = _sut.Build(snapshot, Target, updateDatabaseDefaultCollation: false);
        var steps = plan.Steps;

        Assert.DoesNotContain(steps, s => s.Sql.Contains("Ck_Unrelated_Status"));
        Assert.DoesNotContain(steps, s => s.Sql.Contains("StatusLabel"));
        Assert.DoesNotContain(steps, s => s.Sql.Contains("Idx_Unrelated_Id_Where_IdIsNotNull"));
        Assert.DoesNotContain(steps, s => s.Sql.Contains("vUnrelated"));
    }

    [Fact]
    public void Build_UpdateDatabaseDefaultCollation_DropsDefaultAndCheckConstraintsBeforeTheFunctionTheyCall()
    {
        // Reproduces a real-world failure: a DEFAULT constraint's expression calls a schema-bound
        // function (e.g. "DEFAULT dbo.GetDefaultProcessingDate()"). SQL Server refuses to DROP FUNCTION while that
        // constraint still references it - just like the view-on-view case, but for a constraint
        // referencing a function - so the constraint must be dropped first (and only recreated once the
        // function exists again), even though it is never itself a target a schema-bound object depends
        // on, unlike a computed column.
        var orders = new TableSnapshotBuilder("dbo", "Orders")
            .WithColumn("Name", "varchar", Source.Value)
            .Build();

        var adjustments = new TableSnapshotBuilder("dbo", "AdjustmentRecord")
            .WithColumn("Id", "int", null, isNullable: false)
            .WithColumn("ProcessedAt", "datetime", null)
            .WithDefaultConstraint("Df_AdjustmentRecord_ProcessedAt", "ProcessedAt", "(dbo.GetDefaultProcessingDate())")
            .WithCheckConstraint("Ck_AdjustmentRecord_ProcessedAt", "ProcessedAt", "(dbo.GetDefaultProcessingDate() IS NOT NULL)")
            .Build();

        var snapshot = new DatabaseSnapshotBuilder()
            .WithTable(orders)
            .WithTable(adjustments)
            .WithFunction("dbo", "GetDefaultProcessingDate", "CREATE FUNCTION [dbo].[GetDefaultProcessingDate]() WITH SCHEMABINDING RETURNS datetime AS BEGIN RETURN GETDATE(); END;", isSchemaBound: true)
            .Build();

        var plan = _sut.Build(snapshot, Target, updateDatabaseDefaultCollation: true);
        var steps = plan.Steps.ToList();

        var dropDefault = steps.FindIndex(s => s.Kind == MigrationStepKind.DropDefaultConstraint && s.Sql.Contains("Df_AdjustmentRecord_ProcessedAt"));
        var dropCheck = steps.FindIndex(s => s.Kind == MigrationStepKind.DropCheckConstraint && s.Sql.Contains("Ck_AdjustmentRecord_ProcessedAt"));
        var dropFunction = steps.FindIndex(s => s.Kind == MigrationStepKind.DropSchemaBoundObject && s.Sql.Contains("GetDefaultProcessingDate"));
        var addFunction = steps.FindIndex(s => s.Kind == MigrationStepKind.AddSchemaBoundObject && s.Sql.Contains("GetDefaultProcessingDate"));
        var addDefault = steps.FindIndex(s => s.Kind == MigrationStepKind.AddDefaultConstraint && s.Sql.Contains("Df_AdjustmentRecord_ProcessedAt"));
        var addCheck = steps.FindIndex(s => s.Kind == MigrationStepKind.AddCheckConstraint && s.Sql.Contains("Ck_AdjustmentRecord_ProcessedAt"));

        Assert.True(dropDefault >= 0 && dropCheck >= 0 && dropFunction >= 0 && addFunction >= 0 && addDefault >= 0 && addCheck >= 0);
        Assert.True(dropDefault < dropFunction, "the default constraint must be dropped before the function it calls");
        Assert.True(dropCheck < dropFunction, "the check constraint must be dropped before the function it calls");
        Assert.True(addFunction < addDefault, "the function must be recreated before the default constraint that calls it");
        Assert.True(addFunction < addCheck, "the function must be recreated before the check constraint that calls it");
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
    public void Build_SchemaBoundViewReferencingAnotherSchemaBoundView_DropsWrapperBeforeBaseAndRecreatesInReverse()
    {
        // Reproduces a real-world failure: an indexed base view directly referencing the changing
        // column, wrapped by another schema-bound view that only references the base view (not the
        // table). SchemaBoundDependencies alone would only find the base view; without resolving the
        // wrapper's view-on-view reference too, DROP VIEW on the base view fails with SQL error 3729
        // because the still-present wrapper still schema-binds to it.
        var orders = new TableSnapshotBuilder("dbo", "Orders")
            .WithColumn("Id", "int", null, isNullable: false)
            .WithColumn("CustomerName", "varchar", Source.Value)
            .Build();

        var baseViewRef = new ObjectRef("dbo", "_vOrdersView", DatabaseObjectKind.View);
        var wrapperViewRef = new ObjectRef("dbo", "vOrdersView", DatabaseObjectKind.View);

        var snapshot = new DatabaseSnapshotBuilder()
            .WithTable(orders)
            .WithView("dbo", "_vOrdersView", "CREATE VIEW [dbo].[_vOrdersView] WITH SCHEMABINDING AS SELECT Id, CustomerName FROM dbo.Orders;", isSchemaBound: true)
            .WithView("dbo", "vOrdersView", "CREATE VIEW [dbo].[vOrdersView] WITH SCHEMABINDING AS SELECT Id, CustomerName FROM dbo._vOrdersView;", isSchemaBound: true)
            .WithSchemaBoundDependency(baseViewRef, orders.Ref, "CustomerName")
            .WithSchemaBoundObjectReference(wrapperViewRef, baseViewRef)
            .Build();

        var plan = _sut.Build(snapshot, Target, updateDatabaseDefaultCollation: false);
        var steps = plan.Steps.ToList();

        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropSchemaBoundObject && s.Sql.Contains("_vOrdersView"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropSchemaBoundObject && s.Sql.Contains("vOrdersView"));

        var dropWrapper = steps.FindIndex(s => s.Kind == MigrationStepKind.DropSchemaBoundObject && s.Sql.Contains("[vOrdersView]"));
        var dropBase = steps.FindIndex(s => s.Kind == MigrationStepKind.DropSchemaBoundObject && s.Sql.Contains("[_vOrdersView]"));
        var addBase = steps.FindIndex(s => s.Kind == MigrationStepKind.AddSchemaBoundObject && s.Sql.Contains("[_vOrdersView]"));
        var addWrapper = steps.FindIndex(s => s.Kind == MigrationStepKind.AddSchemaBoundObject && s.Sql.Contains("[vOrdersView]"));

        Assert.True(dropWrapper < dropBase, "the wrapper view must be dropped before the base view it schema-binds to");
        Assert.True(addBase < addWrapper, "the base view must be recreated before the wrapper view that references it");
    }

    [Fact]
    public void Build_SchemaBoundFunctionReferencingTwoOtherSchemaBoundObjects_DoesNotThrowOnDuplicateKey()
    {
        // Regression test: a single dependent (here GetOrderSummary) can reference more than one other
        // schema-bound object in its body. Building the "what does this object reference" lookup as a
        // one-key-per-dependent Dictionary throws ArgumentException ("An item with the same key has
        // already been added") the moment a dependent has two SchemaBoundObjectReferences rows - it must
        // be a one-to-many lookup (grouped list), not a 1:1 dictionary.
        var orders = new TableSnapshotBuilder("dbo", "Orders")
            .WithColumn("Id", "int", null, isNullable: false)
            .WithColumn("CustomerName", "varchar", Source.Value)
            .Build();

        var viewARef = new ObjectRef("dbo", "vOrdersA", DatabaseObjectKind.View);
        var viewBRef = new ObjectRef("dbo", "vOrdersB", DatabaseObjectKind.View);
        var functionRef = new ObjectRef("dbo", "GetOrderSummary", DatabaseObjectKind.Function);

        var snapshot = new DatabaseSnapshotBuilder()
            .WithTable(orders)
            .WithView("dbo", "vOrdersA", "CREATE VIEW [dbo].[vOrdersA] WITH SCHEMABINDING AS SELECT Id, CustomerName FROM dbo.Orders;", isSchemaBound: true)
            .WithView("dbo", "vOrdersB", "CREATE VIEW [dbo].[vOrdersB] WITH SCHEMABINDING AS SELECT Id, CustomerName FROM dbo.Orders;", isSchemaBound: true)
            .WithFunction("dbo", "GetOrderSummary", "CREATE FUNCTION [dbo].[GetOrderSummary]() ... WITH SCHEMABINDING ...", isSchemaBound: true)
            .WithSchemaBoundDependency(viewARef, orders.Ref, "CustomerName")
            .WithSchemaBoundDependency(viewBRef, orders.Ref, "CustomerName")
            .WithSchemaBoundObjectReference(functionRef, viewARef)
            .WithSchemaBoundObjectReference(functionRef, viewBRef)
            .Build();

        var plan = _sut.Build(snapshot, Target, updateDatabaseDefaultCollation: false);
        var steps = plan.Steps.ToList();

        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropSchemaBoundObject && s.Sql.Contains("GetOrderSummary"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropSchemaBoundObject && s.Sql.Contains("vOrdersA"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropSchemaBoundObject && s.Sql.Contains("vOrdersB"));

        var dropFunction = steps.FindIndex(s => s.Kind == MigrationStepKind.DropSchemaBoundObject && s.Sql.Contains("GetOrderSummary"));
        var dropViewA = steps.FindIndex(s => s.Kind == MigrationStepKind.DropSchemaBoundObject && s.Sql.Contains("[vOrdersA]"));
        var dropViewB = steps.FindIndex(s => s.Kind == MigrationStepKind.DropSchemaBoundObject && s.Sql.Contains("[vOrdersB]"));

        Assert.True(dropFunction < dropViewA, "the function must be dropped before either view it references");
        Assert.True(dropFunction < dropViewB, "the function must be dropped before either view it references");
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
