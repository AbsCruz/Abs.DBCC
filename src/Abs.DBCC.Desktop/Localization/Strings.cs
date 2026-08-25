using System.Globalization;

namespace Abs.DBCC.Desktop.Localization;

/// <summary>
/// UI text for both supported languages. The active language is fixed once at startup, based on
/// the OS UI language: German if it's German, English otherwise (see <see cref="IsGerman"/>).
/// </summary>
public static class Strings
{
    public static readonly bool IsGerman =
        string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "de", StringComparison.OrdinalIgnoreCase);

    private static string L(string de, string en) => IsGerman ? de : en;

    // MainWindow
    public static string ConnectedToFormat => L("Verbunden mit: {0} ({1})", "Connected to: {0} ({1})");

    // ConnectionSetupView
    public static string ConnectToDatabaseTitle => L("Verbindung zur Datenbank", "Connect to Database");
    public static string ServerLabel => L("Server", "Server");
    public static string ServerPlaceholder => L("z. B. localhost oder 192.168.1.10", "e.g. localhost or 192.168.1.10");
    public static string DatabaseLabel => L("Datenbank", "Database");
    public static string UserLabel => L("Benutzer", "User");
    public static string PasswordLabel => L("Passwort", "Password");
    public static string TrustServerCertificateLabel => L("Serverzertifikat vertrauen (TrustServerCertificate)", "Trust server certificate (TrustServerCertificate)");
    public static string ContinueButton => L("Weiter", "Continue");
    public static string ConnectionFailedFormat => L("Verbindung fehlgeschlagen: {0}", "Connection failed: {0}");

    // CollationOverviewView
    public static string CollationOverviewTitle => L("Collation-Übersicht", "Collation Overview");
    public static string DatabaseDefaultCollationFormat => L("Datenbank-Default-Collation: {0}", "Database default collation: {0}");
    public static string Loading => L("Lädt...", "Loading...");
    public static string MixedCollationLabel => L("(gemischte Collation)", "(mixed collation)");
    public static string BackButton => L("Zurück", "Back");
    public static string ContinueToTargetCollationButton => L("Weiter zur Ziel-Collation", "Continue to Target Collation");

    // MigrationPlanReviewView
    public static string ReviewMigrationPlanTitle => L("Migrationsplan prüfen", "Review Migration Plan");
    public static string FromLabel => L("Von ", "From ");
    public static string ToLabel => L(" nach ", " to ");
    public static string AffectedTablesFormat => L("Betroffene Tabellen: {0}", "Affected tables: {0}");
    public static string CheckingActiveSessions => L("Prüfe aktive Sitzungen und Datenmenge...", "Checking active sessions and data volume...");
    public static string OtherActiveConnectionsFormat => L("Andere aktive Verbindungen zur Datenbank: {0}", "Other active connections to the database: {0}");
    public static string RecheckActiveConnectionsButton => L("Erneut prüfen", "Check again");
    public static string EstimatedAffectedRowsFormat => L("Geschätzte Zeilen in betroffenen Tabellen: {0}", "Estimated rows in affected tables: {0}");
    public static string TransactionLogFormat => L("Transaktionsprotokoll aktuell: {0}", "Transaction log currently: {0}");
    public static string LogFileSizeFormat => L("{0:F1} MB ({1:F1}% belegt)", "{0:F1} MB ({1:F1}% used)");
    public static string EstimatedVerificationMemoryFormat => L(
        "Geschätzter Speicherbedarf für die Datenprüfung: ~{0} (bei {1:N0} Zeilen in der gesamten Datenbank)",
        "Estimated memory for data verification: ~{0} (based on {1:N0} rows across the whole database)");
    public static string AvailableMemoryFormat => L(
        "Vorhandener Arbeitsspeicher: {0} frei von {1} gesamt",
        "Available memory: {0} free of {1} total");
    public static string MemoryEstimateExceedsAvailableWarning => L(
        "Achtung: Der geschätzte Speicherbedarf übersteigt den aktuell freien Arbeitsspeicher.",
        "Warning: the estimated memory requirement exceeds the currently available memory.");
    public static string ProcessOverviewTitle => L("Ablauf einer Migration:", "How a migration runs:");
    public static string ProcessOverviewStep1 => L("1. Ausgangsdaten aller Tabellen werden zeilenweise gehasht (zur späteren Prüfung).", "1. Every table's current rows are hashed row by row (for later verification).");
    public static string ProcessOverviewStep2Format => L("2. Die {0} Migrationsschritte werden ausgeführt.", "2. The {0} migration steps are executed.");
    public static string ProcessOverviewStep3 => L("3. Die Datenbankstruktur wird geprüft.", "3. The database structure is verified.");
    public static string ProcessOverviewStep4 => L("4. Die Daten aller Tabellen werden erneut gehasht.", "4. Every table's rows are hashed again.");
    public static string ProcessOverviewStep5 => L("5. Vorher- und Nachher-Hashes werden verglichen.", "5. The before and after hashes are compared.");
    public static string SkipDataVerificationLabel => L(
        "Datenprüfung überspringen (z. B. wenn dieser Ablauf bereits mehrfach auf einem Backup/Zweitsystem geprüft wurde)",
        "Skip data verification (e.g. when this run has already been verified repeatedly against a backup/secondary system)");
    public static string StartMigrationButton => L("Migration starten", "Start Migration");
    public static string ExportScriptButton => L("Als SQL-Skript exportieren", "Export as SQL Script");
    public static string SaveScriptDialogTitle => L("SQL-Skript speichern", "Save SQL Script");
    public static string SqlScriptFileLabel => L("SQL-Skriptdatei", "SQL script file");

    // MigrationResultView
    public static string MigrationSucceededTitle => L("Migration erfolgreich", "Migration Succeeded");
    public static string MigrationFailedTitle => L("Migration fehlgeschlagen", "Migration Failed");
    public static string StructuralCheckSucceeded => L("Strukturprüfung erfolgreich - keine Abweichungen.", "Structural check succeeded - no discrepancies.");
    public static string DiscrepanciesFound => L("Es wurden Abweichungen bei der Struktur- oder Datenprüfung gefunden:", "Discrepancies were found during the structural or data check:");
    public static string DataVerificationSkippedNote => L(
        "Hinweis: Die Datenprüfung wurde für diesen Lauf übersprungen.",
        "Note: data verification was skipped for this run.");
    public static string ExportReportButton => L("Bericht exportieren", "Export Report");
    public static string RestartMigrationButton => L("Neue Migration starten", "Start New Migration");
    public static string StepLogLabel => L("Schritt-Log", "Step Log");
    public static string SaveReportDialogTitle => L("Migrationsbericht speichern", "Save Migration Report");
    public static string TextFileLabel => L("Textdatei", "Text file");

    // MigrationResultViewModel.BuildReportText
    public static string ReportHeaderFormat => L("Collation-Migration – {0}", "Collation migration – {0}");
    public static string ReportSucceededWord => L("erfolgreich", "succeeded");
    public static string ReportFailedWord => L("fehlgeschlagen", "failed");
    public static string ReportCreatedFormat => L("Erstellt: {0}", "Created: {0}");
    public static string ReportErrorFormat => L("Fehler: {0}", "Error: {0}");
    public static string ReportStepsLabel => L("Schritte:", "Steps:");
    public static string ReportStepOk => L("OK", "OK");
    public static string ReportStepError => L("FEHLER", "ERROR");
    public static string ReportVerificationFormat => L("Verifikation: {0}", "Verification: {0}");
    public static string ReportNoDiscrepancies => L("keine Abweichungen", "no discrepancies");
    public static string ReportDiscrepanciesFound => L("Abweichungen gefunden", "discrepancies found");
    public static string ReportStructuralLabel => L("Struktur", "Structural");
    public static string ReportDataLabel => L("Daten", "Data");
    public static string ReportDataVerificationSkipped => L("übersprungen für diesen Lauf", "skipped for this run");

    // MigrationRunView
    public static string MigrationRunningTitle => L("Migration läuft...", "Migration in progress...");
    public static string MigrationCancellingTitle => L("Migration wird abgebrochen...", "Cancelling migration...");
    public static string MigrationCancelledTitle => L("Migration abgebrochen.", "Migration cancelled.");
    public static string ConnectionLostTitle => L("Datenbankverbindung verloren.", "Database connection lost.");
    public static string MigrationCompletedTitle => L("Migration abgeschlossen.", "Migration completed.");
    public static string StepsSuffix => L(" Schritte", " steps");
    public static string CancelButton => L("Abbrechen", "Cancel");
    public static string BackToStartButton => L("Zurück zum Start", "Back to Start");

    public static string PhaseCapturingRowsBefore => L("Erfasse Ausgangsdaten (Zeilen-Hashes für die spätere Prüfung)...", "Capturing baseline data (row hashes for later verification)...");
    public static string PhaseExecutingSteps => L("Führe Migrationsschritte aus...", "Executing migration steps...");
    public static string PhaseVerifyingStructure => L("Prüfe Datenbankstruktur...", "Verifying database structure...");
    public static string PhaseCapturingRowsAfter => L("Erfasse Zieldaten (Zeilen-Hashes für die Prüfung)...", "Capturing resulting data (row hashes for verification)...");
    public static string PhaseComparingData => L("Vergleiche Vorher- und Nachher-Daten...", "Comparing before/after data...");
    public static string TablesSuffix => L(" Tabellen", " tables");

    // TargetCollationPickerView
    public static string SelectTargetCollationTitle => L("Ziel-Collation wählen", "Select Target Collation");
    public static string SearchPlaceholder => L("Suchen...", "Search...");
    public static string UpdateDatabaseDefaultCollationLabel => L("Datenbank-Default-Collation ebenfalls ändern", "Also change database default collation");
    public static string BuildPlanButton => L("Migrationsplan erstellen", "Build Migration Plan");
    public static string BuildingPlan => L("Plan wird erstellt...", "Building plan...");
    public static string NoChangesNeededFormat => L(
        "Datenbank und alle Tabellen haben bereits die Collation \"{0}\" – es sind keine Änderungen nötig.",
        "The database and every table already have the \"{0}\" collation - no changes are needed.");
}
