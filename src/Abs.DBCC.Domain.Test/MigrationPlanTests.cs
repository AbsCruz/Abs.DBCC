using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Domain.Test;

public class MigrationPlanTests
{
    private static readonly SqlCollationName Source = new("SQL_Latin1_General_CP1_CI_AS");
    private static readonly SqlCollationName Target = new("Latin1_General_100_CI_AS_SC_UTF8");

    private static readonly DatabaseSnapshot EmptySnapshot = new(
        Source, [], [], [], [], [], [], [], [], [], [], [], []);

    private static readonly IReadOnlyList<ObjectRef> OneAffectedTable =
        [new ObjectRef("dbo", "Orders", DatabaseObjectKind.Table)];

    [Fact]
    public void IsNoOp_False_WhenTablesAreAffected()
    {
        var plan = new MigrationPlan(Source, Target, false, EmptySnapshot, [], OneAffectedTable);

        Assert.False(plan.IsNoOp);
    }

    [Fact]
    public void IsNoOp_True_WhenNoTablesAffectedAndDatabaseCollationNotRequested()
    {
        var plan = new MigrationPlan(Source, Target, false, EmptySnapshot, [], []);

        Assert.True(plan.IsNoOp);
    }

    [Fact]
    public void IsNoOp_True_WhenNoTablesAffectedAndDatabaseAlreadyAtTargetCollation()
    {
        var plan = new MigrationPlan(Target, Target, true, EmptySnapshot, [], []);

        Assert.True(plan.IsNoOp);
    }

    [Fact]
    public void IsNoOp_False_WhenNoTablesAffectedButDatabaseCollationStillNeedsToChange()
    {
        var plan = new MigrationPlan(Source, Target, true, EmptySnapshot, [], []);

        Assert.False(plan.IsNoOp);
    }
}
