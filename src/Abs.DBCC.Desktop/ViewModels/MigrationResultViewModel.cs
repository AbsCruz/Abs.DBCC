using System.Text;
using Abs.DBCC.Desktop.Localization;
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
        text.AppendLine(string.Format(Strings.ReportHeaderFormat, Report.Succeeded ? Strings.ReportSucceededWord : Strings.ReportFailedWord));
        text.AppendLine(string.Format(Strings.ReportCreatedFormat, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));

        if (Report.FailureReason is not null)
        {
            text.AppendLine();
            text.AppendLine(string.Format(Strings.ReportErrorFormat, Report.FailureReason));
        }

        text.AppendLine();
        text.AppendLine(Strings.ReportStepsLabel);
        foreach (var step in Report.StepResults)
        {
            var status = step.Succeeded ? Strings.ReportStepOk : Strings.ReportStepError;
            text.AppendLine($"  [{step.Timestamp:HH:mm:ss}] [{status}] {step.Step.Description}" + (step.Error is null ? "" : $" – {step.Error}"));
        }

        if (Report.Verification is not null)
        {
            text.AppendLine();
            text.AppendLine(string.Format(Strings.ReportVerificationFormat,
                Report.Verification.IsSuccess ? Strings.ReportNoDiscrepancies : Strings.ReportDiscrepanciesFound));

            foreach (var diff in Report.Verification.StructuralDiffs)
                text.AppendLine($"  [{Strings.ReportStructuralLabel}] {diff.ObjectDescription}: {diff.Details}");

            if (Report.Verification.DataVerificationSkipped)
                text.AppendLine($"  [{Strings.ReportDataLabel}] {Strings.ReportDataVerificationSkipped}");
            else
                foreach (var diff in Report.Verification.DataDiffs)
                    text.AppendLine($"  [{Strings.ReportDataLabel}] {diff.TableDescription}: {diff.Details}");
        }

        return text.ToString();
    }
}
