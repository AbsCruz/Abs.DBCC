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
        // The invariant is "right after the last ALTER COLUMN", not "always last" - it happens to be
        // last here only because there's nothing to recreate (see the full-dependency test below).
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

        // OrderCustomerName is already on the target collation, so it proves the FK is dropped because it
        // references the changing Orders.CustomerName, not because of its own collation.
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

        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropForeignKey && s.Sql.Contains("FK_Notes_Orders"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropIndex && s.Sql.Contains("IX_Orders_CustomerName"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropCheckConstraint && s.Sql.Contains("CK_Orders_CustomerName"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropCheckConstraint && s.Sql.Contains("CK_Orders_MultiCol"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropDefaultConstraint && s.Sql.Contains("DF_Orders_CustomerName"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropComputedColumn && s.Sql.Contains("DisplayName"));

        // updateDatabaseDefaultCollation is true, so even a check constraint unrelated to any changing
        // column must be dropped/recreated (see the dedicated sweep test below). The PK is a non-filtered
        // index on a non-changing column, so it stays untouched regardless.
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.DropCheckConstraint && s.Sql.Contains("CK_Orders_Unrelated"));
        Assert.DoesNotContain(steps, s => s.Kind == MigrationStepKind.DropIndex && s.Sql.Contains("PK_Orders"));

        var alterSteps = steps.Where(s => s.Kind == MigrationStepKind.AlterColumnCollation).ToList();
        var alterStep = Assert.Single(alterSteps);
        Assert.Contains("CustomerName", alterStep.Sql);

        Assert.Contains(steps, s => s.Kind == MigrationStepKind.AddComputedColumn && s.Sql.Contains("DisplayName"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.AddDefaultConstraint && s.Sql.Contains("DF_Orders_CustomerName"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.AddCheckConstraint && s.Sql.Contains("CK_Orders_CustomerName"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.CreateIndex && s.Sql.Contains("IX_Orders_CustomerName"));
        Assert.Contains(steps, s => s.Kind == MigrationStepKind.AddForeignKey && s.Sql.Contains("FK_Notes_Orders"));

        int IndexOfFirst(Func<MigrationStep, bool> predicate) => steps.ToList().FindIndex(s => predicate(s));

        Assert.True(IndexOfFirst(s => s.Kind == MigrationStepKind.DropForeignKey) < IndexOfFirst(s => s.Kind == MigrationStepKind.DropIndex));
        Assert.True(IndexOfFirst(s => s.Kind == MigrationStepKind.DropIndex) < IndexOfFirst(s => s.Kind == MigrationStepKind.AlterColumnCollation));
        // ALTER DATABASE must run after every ALTER COLUMN but before any recreate step, since the
        // objects those steps bring back would otherwise still block it.
        Assert.True(IndexOfFirst(s => s.Kind == MigrationStepKind.AlterColumnCollation) < IndexOfFirst(s => s.Kind == MigrationStepKind.AlterDatabaseCollation));
        Assert.True(IndexOfFirst(s => s.Kind == MigrationStepKind.AlterDatabaseCollation) < IndexOfFirst(s => s.Kind == MigrationStepKind.AddComputedColumn));
        Assert.True(IndexOfFirst(s => s.Kind == MigrationStepKind.CreateIndex) < IndexOfFirst(s => s.Kind == MigrationStepKind.AddForeignKey));
    }

    [Fact]
    public void Build_UpdateDatabaseDefaultCollation_SweepsEveryCheckConstraintComputedColumnAndFilteredIndex()
    {
        // ALTER DATABASE ... COLLATE checks dependencies across the whole database, not just the
        // migrating columns - a check constraint, computed column, filtered index or schema-bound view
        // on an unrelated table can still block it, and no catalog view predicts this in advance. So
        // with this flag set, every one of these in the database must be dropped/recreated regardless of
        // which column(s) they touch.
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
        // A default/check constraint whose expression calls a schema-bound function (e.g. "DEFAULT
        // dbo.GetDefaultProcessingDate()") blocks DROP FUNCTION while it exists, so it must be dropped
        // first and only recreated once the function exists again - even though, unlike a computed
        // column, a constraint is never itself something a schema-bound object depends on.
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
    public void Build_ComputedColumnCallingSchemaBoundFunction_DropsColumnBeforeFunctionAndRecreatesAfter()
    {
        // Unlike a constraint, a computed column can also be the *target* a schema-bound view depends on
        // (see the next test), so it can't simply always move earlier. This is the "calls a function"
        // direction: it must still be dropped before, and recreated after, the function it calls.
        var orders = new TableSnapshotBuilder("dbo", "Orders")
            .WithColumn("Name", "varchar", Source.Value)
            .Build();

        var log = new TableSnapshotBuilder("dbo", "RegistrationLog")
            .WithColumn("Id", "int", null, isNullable: false)
            .WithComputedColumn("IsValidFlag", "dbo.GetMinRegistrationDate()")
            .Build();

        var functionRef = new ObjectRef("dbo", "GetMinRegistrationDate", DatabaseObjectKind.Function);
        var snapshot = new DatabaseSnapshotBuilder()
            .WithTable(orders)
            .WithTable(log)
            .WithFunction("dbo", "GetMinRegistrationDate", "CREATE FUNCTION [dbo].[GetMinRegistrationDate]() WITH SCHEMABINDING RETURNS DATETIME AS BEGIN RETURN '2000-01-01'; END;", isSchemaBound: true)
            .WithComputedColumnObjectReference(log.Ref, "IsValidFlag", functionRef)
            .Build();

        var plan = _sut.Build(snapshot, Target, updateDatabaseDefaultCollation: true);
        var steps = plan.Steps.ToList();

        var dropColumn = steps.FindIndex(s => s.Kind == MigrationStepKind.DropComputedColumn && s.Sql.Contains("IsValidFlag"));
        var dropFunction = steps.FindIndex(s => s.Kind == MigrationStepKind.DropSchemaBoundObject && s.Sql.Contains("GetMinRegistrationDate"));
        var addFunction = steps.FindIndex(s => s.Kind == MigrationStepKind.AddSchemaBoundObject && s.Sql.Contains("GetMinRegistrationDate"));
        var addColumn = steps.FindIndex(s => s.Kind == MigrationStepKind.AddComputedColumn && s.Sql.Contains("IsValidFlag"));

        Assert.True(dropColumn >= 0 && dropFunction >= 0 && addFunction >= 0 && addColumn >= 0);
        Assert.True(dropColumn < dropFunction, "the computed column must be dropped before the function it calls");
        Assert.True(addFunction < addColumn, "the function must be recreated before the computed column that calls it");
    }

    [Fact]
    public void Build_ComputedColumnSelectedBySchemaBoundView_DropsViewBeforeColumnAndRecreatesAfter()
    {
        // The opposite direction: here the computed column is the target a schema-bound view depends on,
        // so the view must be dropped first and only recreated once the column exists again.
        var log = new TableSnapshotBuilder("dbo", "RegistrationLog")
            .WithColumn("Id", "int", null, isNullable: false)
            .WithComputedColumn("Label", "'fixed'")
            .Build();

        var viewRef = new ObjectRef("dbo", "RegistrationLogSummary", DatabaseObjectKind.View);
        var snapshot = new DatabaseSnapshotBuilder()
            .WithTable(log)
            .WithView("dbo", "RegistrationLogSummary", "CREATE VIEW [dbo].[RegistrationLogSummary] WITH SCHEMABINDING AS SELECT Id, Label FROM dbo.RegistrationLog;", isSchemaBound: true)
            .WithSchemaBoundDependency(viewRef, log.Ref, "Label")
            .Build();

        var plan = _sut.Build(snapshot, Target, updateDatabaseDefaultCollation: true);
        var steps = plan.Steps.ToList();

        var dropView = steps.FindIndex(s => s.Kind == MigrationStepKind.DropSchemaBoundObject && s.Sql.Contains("RegistrationLogSummary"));
        var dropColumn = steps.FindIndex(s => s.Kind == MigrationStepKind.DropComputedColumn && s.Sql.Contains("Label"));
        var addColumn = steps.FindIndex(s => s.Kind == MigrationStepKind.AddComputedColumn && s.Sql.Contains("Label"));
        var addView = steps.FindIndex(s => s.Kind == MigrationStepKind.AddSchemaBoundObject && s.Sql.Contains("RegistrationLogSummary"));

        Assert.True(dropView >= 0 && dropColumn >= 0 && addColumn >= 0 && addView >= 0);
        Assert.True(dropView < dropColumn, "the view must be dropped before the computed column it selects");
        Assert.True(addColumn < addView, "the computed column must be recreated before the view that selects it");
    }

    [Fact]
    public void Build_ComputedColumnWithNoSchemaBoundRelationship_StillDropsAfterItsOwnIndexAndRecreatesBeforeIt()
    {
        // A computed column with no schema-bound relationship keeps the simpler ordering relative to a
        // regular index built on it, unaffected by the combined schema-bound/computed-column phase.
        var log = new TableSnapshotBuilder("dbo", "RegistrationLog")
            .WithColumn("Id", "int", null, isNullable: false)
            .WithComputedColumn("Label", "'fixed'", persisted: true)
            .WithIndex("IX_RegistrationLog_Label", ["Label"])
            .Build();

        var snapshot = new DatabaseSnapshotBuilder().WithTable(log).Build();

        var plan = _sut.Build(snapshot, Target, updateDatabaseDefaultCollation: true);
        var steps = plan.Steps.ToList();

        var dropIndex = steps.FindIndex(s => s.Kind == MigrationStepKind.DropIndex && s.Sql.Contains("IX_RegistrationLog_Label"));
        var dropColumn = steps.FindIndex(s => s.Kind == MigrationStepKind.DropComputedColumn && s.Sql.Contains("Label"));
        var addColumn = steps.FindIndex(s => s.Kind == MigrationStepKind.AddComputedColumn && s.Sql.Contains("Label"));
        var addIndex = steps.FindIndex(s => s.Kind == MigrationStepKind.CreateIndex && s.Sql.Contains("IX_RegistrationLog_Label"));

        Assert.True(dropIndex >= 0 && dropColumn >= 0 && addColumn >= 0 && addIndex >= 0);
        Assert.True(dropIndex < dropColumn, "the index must be dropped before the computed column it covers");
        Assert.True(addColumn < addIndex, "the computed column must be recreated before the index that covers it");
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
    public void Build_IndexedViewWithClusteredAndNonclusteredIndex_RecreatesClusteredFirstAndDropsClusteredLast()
    {
        // SQL Server rejects creating a nonclustered index on an indexed view before its one unique
        // clustered index exists, and rejects dropping that clustered index while a nonclustered one
        // still exists. The snapshot lists the indexes in reverse of clustered-first order here, so the
        // plan builder - not incidental input ordering - must be what puts them in the right sequence.
        var products = new TableSnapshotBuilder("dbo", "Products")
            .WithColumn("Id", "int", null, isNullable: false)
            .WithColumn("Name", "varchar", Source.Value)
            .Build();

        var viewRef = new ObjectRef("dbo", "ProductSummaryView", DatabaseObjectKind.View);
        var nonclusteredIndex = new IndexSnapshot(
            "IX_ProductSummaryView_Name", false, false, false, false, [new IndexColumnSnapshot("Name", false, false)], null);
        var clusteredIndex = new IndexSnapshot(
            "UQ_ProductSummaryView_Id", true, true, false, false, [new IndexColumnSnapshot("Id", false, false)], null);

        var snapshot = new DatabaseSnapshotBuilder()
            .WithTable(products)
            .WithView("dbo", "ProductSummaryView", "CREATE VIEW [dbo].[ProductSummaryView] WITH SCHEMABINDING AS SELECT Id, Name FROM dbo.Products;", isSchemaBound: true)
            .WithSchemaBoundDependency(viewRef, products.Ref, "Name")
            .WithViewIndex(viewRef, nonclusteredIndex)
            .WithViewIndex(viewRef, clusteredIndex)
            .Build();

        var plan = _sut.Build(snapshot, Target, updateDatabaseDefaultCollation: false);
        var steps = plan.Steps.ToList();

        var dropNonclustered = steps.FindIndex(s => s.Kind == MigrationStepKind.DropIndex && s.Sql.Contains("IX_ProductSummaryView_Name"));
        var dropClustered = steps.FindIndex(s => s.Kind == MigrationStepKind.DropIndex && s.Sql.Contains("UQ_ProductSummaryView_Id"));
        var addClustered = steps.FindIndex(s => s.Kind == MigrationStepKind.CreateIndex && s.Sql.Contains("UQ_ProductSummaryView_Id"));
        var addNonclustered = steps.FindIndex(s => s.Kind == MigrationStepKind.CreateIndex && s.Sql.Contains("IX_ProductSummaryView_Name"));

        Assert.True(dropNonclustered < dropClustered, "the nonclustered index must be dropped before the clustered index that materializes the view");
        Assert.True(addClustered < addNonclustered, "the clustered index must be recreated before any nonclustered index on the same view");
    }

    [Fact]
    public void Build_SchemaBoundViewReferencingAnotherSchemaBoundView_DropsWrapperBeforeBaseAndRecreatesInReverse()
    {
        // The wrapper view only references the base view, not the table, so SchemaBoundDependencies
        // alone would miss it; without resolving that view-on-view reference too, DROP VIEW on the base
        // view fails with SQL error 3729 because the still-present wrapper still schema-binds to it.
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
        // A single dependent (GetOrderSummary) can reference more than one other schema-bound object in
        // its body, so the "what does this reference" lookup must be one-to-many, not a 1:1 dictionary.
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

        // PK_Articles is unrelated to the changing Title column, so the full-text index must be
        // dropped/recreated entirely on its own, without touching any regular index.
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

        Assert.Single(plan.Steps);
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
