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
    public static string TestConnectionButton => L("Verbindung testen", "Test Connection");
    public static string ContinueButton => L("Weiter", "Continue");
    public static string ConnectionSucceeded => L("Verbindung erfolgreich.", "Connection succeeded.");
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
    public static string StartMigrationButton => L("Migration starten", "Start Migration");
    public static string ExportScriptButton => L("Als SQL-Skript exportieren", "Export as SQL Script");
    public static string SaveScriptDialogTitle => L("SQL-Skript speichern", "Save SQL Script");
    public static string SqlScriptFileLabel => L("SQL-Skriptdatei", "SQL script file");

    // MigrationResultView
    public static string MigrationSucceededTitle => L("Migration erfolgreich", "Migration Succeeded");
    public static string MigrationFailedTitle => L("Migration fehlgeschlagen", "Migration Failed");
    public static string StructuralCheckSucceeded => L("Strukturprüfung erfolgreich - keine Abweichungen.", "Structural check succeeded - no discrepancies.");
    public static string DiscrepanciesFound => L("Es wurden Abweichungen bei der Struktur- oder Datenprüfung gefunden:", "Discrepancies were found during the structural or data check:");
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

    // MigrationRunView
    public static string MigrationRunningTitle => L("Migration läuft...", "Migration in progress...");
    public static string MigrationCancellingTitle => L("Migration wird abgebrochen...", "Cancelling migration...");
    public static string MigrationCancelledTitle => L("Migration abgebrochen.", "Migration cancelled.");
    public static string ConnectionLostTitle => L("Datenbankverbindung verloren.", "Database connection lost.");
    public static string MigrationCompletedTitle => L("Migration abgeschlossen.", "Migration completed.");
    public static string StepsSuffix => L(" Schritte", " steps");
    public static string CancelButton => L("Abbrechen", "Cancel");
    public static string BackToStartButton => L("Zurück zum Start", "Back to Start");

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
