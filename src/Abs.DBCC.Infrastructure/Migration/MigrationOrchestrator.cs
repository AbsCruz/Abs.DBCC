using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Migration;

namespace Abs.DBCC.Infrastructure.Migration;

/// <summary>
/// Executes a plan's steps in order against one open connection.
///
/// When it does, SQL Server won't run ALTER DATABASE ... COLLATE inside an explicit transaction, and
/// refuses it entirely while a dropped-but-not-yet-recreated object still exists in its old form (see
/// MigrationPlanBuilder). So execution splits into three segments: (1) all drops and ALTER COLUMNs, in
/// one transaction; (2) the ALTER DATABASE statement, outside any transaction; (3) all recreate steps, in
/// a second transaction. A segment-1 failure rolls back cleanly. A segment-2/3 failure can't roll back to
/// the original state (some changes are already committed) but never corrupts data - it leaves a safe,
/// resumable state that a human finishes, typically by re-running the pending recreate steps.
///
/// Cancellation is handled the same way: in segment 1 it rolls back cleanly (using a fresh,
/// non-cancelled token, since the caller's own token is already the reason the rollback is needed) and
/// rethrows as a normal <see cref="OperationCanceledException"/>. Past that point - including the
/// transaction-boundary calls themselves (BeginTransactionAsync/CommitAsync) - a cancellation, or any
/// other exception, is exactly as uncertain in outcome as an ordinary step failure there, so it is
/// reported as the same kind of partial-state <see cref="MigrationReport"/> rather than a bare exception
/// that would misleadingly read as "nothing happened".
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
        await runner.BeginTransactionAsync(ct: ct);

        foreach (var step in stepsBeforeDatabaseCollation)
        {
            MigrationStepResult result;
            try
            {
                result = await ExecuteStepAsync(runner, step, ct);
            }
            catch (OperationCanceledException)
            {
                // The step itself was cancelled, not failed - roll back with a fresh token so the
                // rollback statement actually gets sent instead of throwing immediately because the
                // token handed to it is already cancelled (see TryRollbackAsync's own doc comment,
                // which is about a genuinely dropped connection, a different situation).
                await TryRollbackAsync(runner, CancellationToken.None);
                throw;
            }

            results.Add(result);
            progress?.Report(result);

            if (!result.Succeeded)
            {
                var rollbackNote = await TryRollbackAsync(runner, CancellationToken.None);
                return new MigrationReport(false, results, $"Schritt '{step.Description}' fehlgeschlagen: {result.Error}{rollbackNote}", null);
            }
        }

        try
        {
            await runner.CommitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Unlike a step failing, a cancelled COMMIT is genuinely ambiguous: SQL Server may have
            // already applied it server-side before the client gave up waiting for the acknowledgment,
            // or it may not have. Either way this must not read as a clean "cancelled" - the caller
            // needs to know the outcome is uncertain rather than assuming nothing happened.
            return new MigrationReport(false, results,
                "Der Migrationsvorgang wurde abgebrochen, während die Spaltenänderungen committed wurden. Ob diese " +
                "Änderungen tatsächlich übernommen wurden, ist ungewiss - prüfen Sie den aktuellen Datenbankzustand, " +
                "bevor Sie fortfahren.", null);
        }
        catch (Exception ex)
        {
            // A timeout, dropped connection, or other SqlException here is exactly as ambiguous as a
            // cancellation - it must not be allowed to escape to a generic unexpected-error screen that
            // omits this specific uncertainty about whether the commit actually took effect.
            return new MigrationReport(false, results,
                $"Beim Committen der Spaltenänderungen ist ein Fehler aufgetreten: {ex.Message}. Ob diese Änderungen " +
                "tatsächlich übernommen wurden, ist ungewiss - prüfen Sie den aktuellen Datenbankzustand, bevor Sie " +
                "fortfahren.", null);
        }

        if (databaseCollationStep is not null)
        {
            MigrationStepResult result;
            try
            {
                result = await ExecuteStepAsync(runner, databaseCollationStep, ct);
            }
            catch (OperationCanceledException)
            {
                // ALTER DATABASE cannot run inside a transaction, so cancelling while it is in flight
                // leaves it genuinely uncertain whether SQL Server already applied it before the client
                // gave up waiting - unlike a segment-1 cancellation (safely rolled back) this must not
                // be reported as a clean "cancelled", but as a partial state needing manual follow-up.
                return new MigrationReport(false, results,
                    "Der Migrationsvorgang wurde abgebrochen, während die Datenbank-Default-Collation gesetzt wurde. Ob dieser " +
                    "Schritt selbst noch erfolgreich abgeschlossen wurde, ist ungewiss - prüfen Sie die aktuelle Datenbank-Collation. " +
                    "Die zuvor entfernten Indizes/Constraints/Objekte sind in jedem Fall noch nicht wiederhergestellt und müssen " +
                    "manuell nachgeholt werden.", null);
            }

            results.Add(result);
            progress?.Report(result);

            if (!result.Succeeded)
            {
                // ALTER DATABASE runs outside a transaction, so an ordinary error here (a timeout, a
                // dropped connection) is exactly as uncertain in outcome as a cancellation at the same
                // point - SQL Server may have already applied the collation change before the error was
                // reported back, so this must not categorically claim the step failed.
                return new MigrationReport(false, results,
                    $"Alle Spalten wurden erfolgreich migriert, aber beim Setzen der Datenbank-Default-Collation ist ein Fehler " +
                    $"aufgetreten: {result.Error}. Da dieser Schritt außerhalb einer Transaktion läuft, ist nicht sicher " +
                    "auszuschließen, dass SQL Server die Änderung dennoch übernommen hat, bevor der Fehler zurückgemeldet wurde - " +
                    "prüfen Sie die aktuelle Datenbank-Collation. Die zuvor entfernten Indizes/Constraints/Objekte sind in jedem " +
                    "Fall noch nicht wiederhergestellt.", null);
            }
        }

        if (stepsAfterDatabaseCollation.Count > 0)
        {
            try
            {
                await runner.BeginTransactionAsync(ct: ct);
            }
            catch (OperationCanceledException)
            {
                // The database collation is already committed at this point, but no recreate step has
                // run yet - a bare cancellation exception would still misleadingly read as "cancelled,
                // nothing happened" here.
                return new MigrationReport(false, results,
                    "Spalten und Datenbank-Default-Collation wurden erfolgreich geändert, aber der Migrationsvorgang " +
                    "wurde abgebrochen, bevor die Wiederherstellung der entfernten Objekte begonnen hat. Manuelles " +
                    "Nacharbeiten erforderlich.", null);
            }
            catch (Exception ex)
            {
                return new MigrationReport(false, results,
                    "Spalten und Datenbank-Default-Collation wurden erfolgreich geändert, aber das Öffnen der Transaktion " +
                    $"für die Wiederherstellung der entfernten Objekte ist fehlgeschlagen: {ex.Message}. Manuelles " +
                    "Nacharbeiten erforderlich.", null);
            }

            foreach (var step in stepsAfterDatabaseCollation)
            {
                MigrationStepResult result;
                try
                {
                    result = await ExecuteStepAsync(runner, step, ct);
                }
                catch (OperationCanceledException)
                {
                    // Past the irreversible boundary (the database collation is already committed), so
                    // this must not surface as a plain, generically-worded "cancelled" - the caller
                    // needs the same explicit partial-state/recovery report as any other segment-3
                    // failure, not just a bare exception implying nothing happened.
                    var rollbackNote = await TryRollbackAsync(runner, CancellationToken.None);
                    return new MigrationReport(false, results,
                        "Spalten und Datenbank-Default-Collation wurden erfolgreich geändert, aber der Migrationsvorgang wurde " +
                        $"während des Wiederherstellens der entfernten Objekte bei Schritt '{step.Description}' abgebrochen. " +
                        $"Manuelles Nacharbeiten erforderlich.{rollbackNote}", null);
                }

                results.Add(result);
                progress?.Report(result);

                if (!result.Succeeded)
                {
                    var rollbackNote = await TryRollbackAsync(runner, CancellationToken.None);
                    return new MigrationReport(false, results,
                        $"Spalten und Datenbank-Default-Collation wurden erfolgreich geändert, aber das Wiederherstellen der entfernten " +
                        $"Objekte ist bei Schritt '{step.Description}' fehlgeschlagen: {result.Error}. Manuelles Nacharbeiten erforderlich.{rollbackNote}", null);
                }
            }

            try
            {
                await runner.CommitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return new MigrationReport(false, results,
                    "Spalten und Datenbank-Default-Collation wurden erfolgreich geändert, und die entfernten Objekte " +
                    "wurden wiederhergestellt, aber der Migrationsvorgang wurde abgebrochen, während diese Änderungen " +
                    "committed wurden. Ob sie tatsächlich übernommen wurden, ist ungewiss - prüfen Sie den aktuellen " +
                    "Zustand der betroffenen Objekte, bevor Sie fortfahren.", null);
            }
            catch (Exception ex)
            {
                return new MigrationReport(false, results,
                    "Spalten und Datenbank-Default-Collation wurden erfolgreich geändert, und die entfernten Objekte " +
                    $"wurden wiederhergestellt, aber beim Committen dieser Änderungen ist ein Fehler aufgetreten: {ex.Message}. " +
                    "Ob sie tatsächlich übernommen wurden, ist ungewiss - prüfen Sie den aktuellen Zustand der betroffenen " +
                    "Objekte, bevor Sie fortfahren.", null);
            }
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
        catch (OperationCanceledException)
        {
            // Cancellation is not a step failure - it must propagate as a cancellation so the caller
            // rolls back with a fresh token instead of this being reported as a SQL error.
            throw;
        }
        catch (Exception ex)
        {
            return new MigrationStepResult(step, false, ex.Message, DateTime.Now);
        }
    }
}
