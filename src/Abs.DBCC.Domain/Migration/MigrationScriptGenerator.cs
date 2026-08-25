using System.Text;

namespace Abs.DBCC.Domain.Migration;

/// <summary>
/// Renders a <see cref="MigrationPlan"/> as a stand-alone T-SQL script reproducing exactly what
/// <c>MigrationOrchestrator</c> would execute, for running (or reviewing, or handing to a DBA) outside the
/// application, e.g. via sqlcmd or SSMS.
///
/// Transaction structure mirrors the orchestrator: without a database-default-collation change, everything
/// runs in one transaction. With one, SQL Server forbids <c>ALTER DATABASE ... COLLATE</c> inside an
/// explicit transaction, forcing a three-segment split (drop+alter, then the ALTER DATABASE statement
/// alone, then the recreate steps). Each step is its own batch (<c>GO</c>) because recreating a view,
/// function, procedure, or trigger must be the only statement in its batch.
/// </summary>
public static class MigrationScriptGenerator
{
    public static string Generate(MigrationPlan plan, string? databaseName = null)
    {
        var script = new StringBuilder();

        AppendHeader(script, plan, databaseName);

        var databaseCollationIndex = plan.Steps.ToList().FindIndex(s => s.Kind == MigrationStepKind.AlterDatabaseCollation);
        var stepsBeforeDatabaseCollation = databaseCollationIndex < 0 ? plan.Steps : plan.Steps.Take(databaseCollationIndex).ToList();
        var databaseCollationStep = databaseCollationIndex < 0 ? null : plan.Steps[databaseCollationIndex];
        var stepsAfterDatabaseCollation = databaseCollationIndex < 0 ? [] : plan.Steps.Skip(databaseCollationIndex + 1).ToList();

        script.AppendLine("SET XACT_ABORT ON;");
        script.AppendLine("GO");
        script.AppendLine();

        AppendTransactionBlock(script, stepsBeforeDatabaseCollation);

        if (databaseCollationStep is not null)
        {
            script.AppendLine("-- The statements above are committed. SQL Server does not allow ALTER DATABASE ... COLLATE");
            script.AppendLine("-- inside an explicit transaction, so this statement runs on its own, outside of any");
            script.AppendLine("-- transaction. If it fails, the column changes above remain in place, but the objects");
            script.AppendLine("-- dropped above are not yet recreated - re-run the statements below manually in that case.");
            AppendStep(script, databaseCollationStep);

            AppendTransactionBlock(script, stepsAfterDatabaseCollation);
        }

        script.AppendLine("-- Migration complete.");

        return script.ToString();
    }

    private static void AppendHeader(StringBuilder script, MigrationPlan plan, string? databaseName)
    {
        script.AppendLine("-- ============================================================================");
        script.AppendLine("-- Abs.DBCC collation migration script");
        if (!string.IsNullOrWhiteSpace(databaseName))
            script.AppendLine($"-- Database: {databaseName}");
        script.AppendLine($"-- Source collation: {plan.SourceCollation.Value}");
        script.AppendLine($"-- Target collation: {plan.TargetCollation.Value}");
        script.AppendLine($"-- Update database default collation: {(plan.UpdateDatabaseDefaultCollation ? "yes" : "no")}");
        script.AppendLine($"-- Affected tables: {plan.AffectedTables.Count}");
        script.AppendLine("--");
        script.AppendLine("-- Review this script carefully before running it against a production database. It");
        script.AppendLine("-- performs exactly the steps the Abs.DBCC desktop application would execute for this");
        script.AppendLine("-- migration plan. This script does not verify the result afterwards - use the desktop");
        script.AppendLine("-- application's verification step, or compare manually, once it has finished.");
        script.AppendLine("-- ============================================================================");
        script.AppendLine();
    }

    private static void AppendTransactionBlock(StringBuilder script, IReadOnlyList<MigrationStep> steps)
    {
        if (steps.Count == 0)
            return;

        script.AppendLine("BEGIN TRANSACTION;");
        script.AppendLine("GO");
        script.AppendLine();

        foreach (var step in steps)
            AppendStep(script, step);

        script.AppendLine("COMMIT TRANSACTION;");
        script.AppendLine("GO");
        script.AppendLine();
    }

    private static void AppendStep(StringBuilder script, MigrationStep step)
    {
        script.AppendLine($"-- {step.Description}");
        script.AppendLine(step.Sql);
        script.AppendLine("GO");
        script.AppendLine();
    }
}
