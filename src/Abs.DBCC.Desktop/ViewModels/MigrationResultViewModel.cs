using System.Text;
using Abs.DBCC.Domain.Migration;
using CommunityToolkit.Mvvm.Input;

namespace Abs.DBCC.Desktop.ViewModels;

public partial class MigrationResultViewModel(MigrationReport report) : ViewModelBase
{
    public MigrationReport Report { get; } = report;

    public event EventHandler? RestartRequested;

    /// <summary>Raised with the formatted report text; the view handles the actual save-file dialog.</summary>
    public event EventHandler<string>? ExportRequested;

    [RelayCommand]
    private void Restart() => RestartRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ExportReport() => ExportRequested?.Invoke(this, BuildReportText());

    public string BuildReportText()
    {
        var text = new StringBuilder();
        text.AppendLine($"Collation-Migration – {(Report.Succeeded ? "erfolgreich" : "fehlgeschlagen")}");
        text.AppendLine($"Erstellt: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        if (Report.FailureReason is not null)
        {
            text.AppendLine();
            text.AppendLine($"Fehler: {Report.FailureReason}");
        }

        text.AppendLine();
        text.AppendLine("Schritte:");
        foreach (var step in Report.StepResults)
        {
            var status = step.Succeeded ? "OK" : "FEHLER";
            text.AppendLine($"  [{status}] {step.Step.Description}" + (step.Error is null ? "" : $" – {step.Error}"));
        }

        if (Report.Verification is not null)
        {
            text.AppendLine();
            text.AppendLine($"Verifikation: {(Report.Verification.IsSuccess ? "keine Abweichungen" : "Abweichungen gefunden")}");

            foreach (var diff in Report.Verification.StructuralDiffs)
                text.AppendLine($"  [Struktur] {diff.ObjectDescription}: {diff.Details}");

            foreach (var diff in Report.Verification.DataDiffs)
                text.AppendLine($"  [Daten] {diff.TableDescription}: {diff.Details}");
        }

        return text.ToString();
    }
}
