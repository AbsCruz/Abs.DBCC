using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Migration;

namespace Abs.DBCC.Infrastructure.Migration;

/// <summary>
/// Executes a plan's steps in order against one open connection.
///
/// When the plan does not touch the database's default collation (the common case), every step runs
/// inside a single transaction, so a failure at any point rolls back to leave the database completely
/// unchanged.
///
/// When it does, SQL Server's own rules force a weaker guarantee: ALTER DATABASE ... COLLATE cannot run
/// inside an explicit transaction, and it refuses to run at all while a collation-dependent object
/// (dropped earlier, not yet recreated - see MigrationPlanBuilder) still exists in its OLD, un-recreated
/// form. So execution splits into three segments: (1) everything up to and including the last drop and
/// every ALTER COLUMN, in one transaction; (2) the ALTER DATABASE statement itself, outside any
/// transaction; (3) every recreate step, in a second transaction. A failure in segment 1 rolls back to a
/// fully unchanged database, same as always. A failure in segment 2 or 3 cannot be rolled back to that
/// same starting point (the database COLLATE and/or some recreated objects are already committed), but
/// it never corrupts or loses data - it leaves a safe, described, resumable state that a human needs to
/// finish (typically by re-running the recreate steps still listed as pending in the report).
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
    /// Attempts the rollback defensively: if the connection was lost mid-step (e.g. the network dropped,
    /// or the server killed the session), SQL Server has already rolled the transaction back on its own
    /// server-side, and the rollback call here fails not because anything is inconsistent but simply
    /// because there is no live connection left to send it over. That failure must not be allowed to
    /// propagate as a second, unhandled exception masking the original step failure - it is swallowed
    /// and turned into an explanatory note for the report instead.
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
