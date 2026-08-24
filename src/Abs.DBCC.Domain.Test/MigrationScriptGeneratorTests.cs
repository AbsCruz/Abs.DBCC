using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Domain.Test;

public class MigrationScriptGeneratorTests
{
    private static readonly SqlCollationName Source = new("SQL_Latin1_General_CP1_CI_AS");
    private static readonly SqlCollationName Target = new("Latin1_General_100_CI_AS_SC_UTF8");

    private static readonly DatabaseSnapshot EmptySnapshot = new(
        Source, [], [], [], [], [], [], [], [], [], [], [], []);

    private static MigrationPlan Plan(bool updateDatabaseDefaultCollation, params MigrationStep[] steps) =>
        new(Source, Target, updateDatabaseDefaultCollation, EmptySnapshot, steps, []);

    [Fact]
    public void Generate_WithoutDatabaseCollationChange_WrapsAllStepsInOneTransaction()
    {
        var plan = Plan(false,
            new MigrationStep(0, MigrationStepKind.DropIndex, "Index [IX_1] entfernen", "DROP INDEX [IX_1] ON [dbo].[Orders];"),
            new MigrationStep(1, MigrationStepKind.AlterColumnCollation, "Collation von [dbo].[Orders].[Name] ändern",
                "ALTER TABLE [dbo].[Orders] ALTER COLUMN [Name] nvarchar(50) COLLATE Latin1_General_100_CI_AS_SC_UTF8;"));

        var script = MigrationScriptGenerator.Generate(plan);

        Assert.Contains("SET XACT_ABORT ON;", script);
        Assert.Equal(1, Count(script, "BEGIN TRANSACTION;"));
        Assert.Equal(1, Count(script, "COMMIT TRANSACTION;"));
        Assert.Contains("DROP INDEX [IX_1] ON [dbo].[Orders];", script);
        Assert.Contains("ALTER TABLE [dbo].[Orders] ALTER COLUMN [Name]", script);
        Assert.DoesNotContain("ALTER DATABASE", script);

        // The transaction must open before the first step and close after the last one.
        Assert.True(script.IndexOf("BEGIN TRANSACTION;", StringComparison.Ordinal) <
                     script.IndexOf("DROP INDEX", StringComparison.Ordinal));
        Assert.True(script.IndexOf("ALTER TABLE", StringComparison.Ordinal) <
                     script.IndexOf("COMMIT TRANSACTION;", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_WithDatabaseCollationChange_SplitsIntoThreeSegments()
    {
        var plan = Plan(true,
            new MigrationStep(0, MigrationStepKind.AlterColumnCollation, "alter", "ALTER TABLE [dbo].[Orders] ALTER COLUMN [Name] ..."),
            new MigrationStep(1, MigrationStepKind.AlterDatabaseCollation, "db collation", "ALTER DATABASE CURRENT COLLATE Latin1_General_100_CI_AS_SC_UTF8;"),
            new MigrationStep(2, MigrationStepKind.CreateIndex, "recreate", "CREATE INDEX [IX_1] ON [dbo].[Orders] ([Name]);"));

        var script = MigrationScriptGenerator.Generate(plan);

        Assert.Equal(2, Count(script, "BEGIN TRANSACTION;"));
        Assert.Equal(2, Count(script, "COMMIT TRANSACTION;"));
        Assert.Contains("ALTER DATABASE CURRENT COLLATE Latin1_General_100_CI_AS_SC_UTF8;", script);

        // The ALTER DATABASE statement must sit strictly between the two transactions.
        var firstCommit = script.IndexOf("COMMIT TRANSACTION;", StringComparison.Ordinal);
        var alterDatabase = script.IndexOf("ALTER DATABASE CURRENT", StringComparison.Ordinal);
        var secondBegin = script.LastIndexOf("BEGIN TRANSACTION;", StringComparison.Ordinal);
        Assert.True(firstCommit < alterDatabase);
        Assert.True(alterDatabase < secondBegin);
    }

    [Fact]
    public void Generate_IncludesHeaderWithCollationsAndAffectedTableCount()
    {
        var plan = new MigrationPlan(Source, Target, true, EmptySnapshot, [],
            [new ObjectRef("dbo", "Orders", DatabaseObjectKind.Table)]);

        var script = MigrationScriptGenerator.Generate(plan, databaseName: "MyDb");

        Assert.Contains("Database: MyDb", script);
        Assert.Contains(Source.Value, script);
        Assert.Contains(Target.Value, script);
        Assert.Contains("Affected tables: 1", script);
        Assert.Contains("Update database default collation: yes", script);
    }

    [Fact]
    public void Generate_EachStepIsItsOwnBatch()
    {
        var plan = Plan(false,
            new MigrationStep(0, MigrationStepKind.AddSchemaBoundObject, "View neu erstellen", "CREATE VIEW [dbo].[V1] AS SELECT 1 AS X;"));

        var script = MigrationScriptGenerator.Generate(plan);

        // CREATE VIEW must be the only statement in its batch - GO must immediately follow it.
        var createViewIndex = script.IndexOf("CREATE VIEW", StringComparison.Ordinal);
        var nextGoIndex = script.IndexOf("GO", createViewIndex, StringComparison.Ordinal);
        var textBetween = script[(createViewIndex + "CREATE VIEW [dbo].[V1] AS SELECT 1 AS X;".Length)..nextGoIndex].Trim();
        Assert.Equal(string.Empty, textBetween);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
