using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Migration;

namespace Abs.DBCC.Infrastructure.Migration;

/// <summary>
/// Executes a plan's steps in order against one open connection.
///
/// When the plan doesn't touch the database's default collation, every step runs in a single
/// transaction, so any failure rolls back to a fully unchanged database.
///
/// When it does, SQL Server won't run ALTER DATABASE ... COLLATE inside an explicit transaction, and
/// refuses it entirely while a dropped-but-not-yet-recreated object still exists in its old form (see
/// MigrationPlanBuilder). So execution splits into three segments: (1) all drops and ALTER COLUMNs, in
/// one transaction; (2) the ALTER DATABASE statement, outside any transaction; (3) all recreate steps, in
/// a second transaction. A segment-1 failure rolls back cleanly. A segment-2/3 failure can't roll back to
/// the original state (some changes are already committed) but never corrupts data - it leaves a safe,
/// resumable state that a human finishes, typically by re-running the pending recreate steps.
/// </summary>
public sealed class MigrationOrchestrator(ISqlScriptRunnerFactory runnerFactory) : IMigrationOrchestrator
{
    public async Task<MigrationReport> ExecuteAsync(
        ConnectionProfile profile,
        Domain.Migration.MigrationPlan plan,
        IProgress<MigrationStepResult>? progress = null,
        CancellationToken ct = default)
    {
        var databaseCollationIndex = plan.Steps.ToList().FindIndex(s => s.Kind == MigrationStepKind.AlterDatabaseCollation);

        var stepsBeforeDatabaseCollation = databaseCollationIndex < 0 ? plan.Steps : plan.Steps.Take(databaseCollationIndex).ToList();
        var databaseCollationStep = databaseCollationIndex < 0 ? null : plan.Steps[databaseCollationIndex];
        var stepsAfterDatabaseCollation = databaseCollationIndex < 0 ? [] : plan.Steps.Skip(databaseCollationIndex + 1).ToList();

        var results = new List<MigrationStepResult>();

        await using var runner = await runnerFactory.CreateAsync(profile, ct);
        await runner.BeginTransactionAsync(ct);

        foreach (var step in stepsBeforeDatabaseCollation)
        {
            var result = await ExecuteStepAsync(runner, step, ct);
            results.Add(result);
            progress?.Report(result);

            if (!result.Succeeded)
            {
                var rollbackNote = await TryRollbackAsync(runner, ct);
                return new MigrationReport(false, results, $"Schritt '{step.Description}' fehlgeschlagen: {result.Error}{rollbackNote}", null);
            }
        }

        await runner.CommitAsync(ct);

        if (databaseCollationStep is not null)
        {
            var result = await ExecuteStepAsync(runner, databaseCollationStep, ct);
            results.Add(result);
            progress?.Report(result);

            if (!result.Succeeded)
            {
                return new MigrationReport(false, results,
                    $"Alle Spalten wurden erfolgreich migriert, aber das Setzen der Datenbank-Default-Collation ist fehlgeschlagen: {result.Error}. " +
                    "Die zuvor entfernten Indizes/Constraints/Objekte sind noch nicht wiederhergestellt.", null);
            }
        }

        if (stepsAfterDatabaseCollation.Count > 0)
        {
            await runner.BeginTransactionAsync(ct);

            foreach (var step in stepsAfterDatabaseCollation)
            {
                var result = await ExecuteStepAsync(runner, step, ct);
                results.Add(result);
                progress?.Report(result);

                if (!result.Succeeded)
                {
                    var rollbackNote = await TryRollbackAsync(runner, ct);
                    return new MigrationReport(false, results,
                        $"Spalten und Datenbank-Default-Collation wurden erfolgreich geändert, aber das Wiederherstellen der entfernten " +
                        $"Objekte ist bei Schritt '{step.Description}' fehlgeschlagen: {result.Error}. Manuelles Nacharbeiten erforderlich.{rollbackNote}", null);
                }
            }

            await runner.CommitAsync(ct);
        }

        return new MigrationReport(true, results, null, null);
    }

    /// <summary>
    /// If the connection was lost mid-step, SQL Server already rolled the transaction back server-side,
    /// and this call fails only because there's no live connection left to send it over. That failure is
    /// swallowed into an explanatory note rather than masking the original step failure.
    /// </summary>
    private static async Task<string> TryRollbackAsync(ISqlScriptRunner runner, CancellationToken ct)
    {
        try
        {
            await runner.RollbackAsync(ct);
            return string.Empty;
        }
        catch (Exception ex)
        {
            return " (Hinweis: Der Rollback-Versuch selbst schlug ebenfalls fehl, vermutlich weil die " +
                   $"Datenbankverbindung bereits abgebrochen ist: {ex.Message}. SQL Server rollt eine offene " +
                   "Transaktion in diesem Fall serverseitig automatisch zurück, sodass die Datenbank dennoch " +
                   "unverändert bleibt.)";
        }
    }

    private static async Task<MigrationStepResult> ExecuteStepAsync(ISqlScriptRunner runner, MigrationStep step, CancellationToken ct)
    {
        try
        {
            await runner.ExecuteNonQueryAsync(step.Sql, ct: ct);
            return new MigrationStepResult(step, true, null, DateTime.Now);
        }
        catch (Exception ex)
        {
            return new MigrationStepResult(step, false, ex.Message, DateTime.Now);
        }
    }
}
