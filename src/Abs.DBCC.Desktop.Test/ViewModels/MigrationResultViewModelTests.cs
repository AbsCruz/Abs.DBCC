using Abs.DBCC.Desktop.Localization;
using Abs.DBCC.Desktop.ViewModels;
using Abs.DBCC.Domain.Migration;

namespace Abs.DBCC.Desktop.Test.ViewModels;

public class MigrationResultViewModelTests
{
    private static MigrationStep Step(string description) => new(0, MigrationStepKind.AlterColumnCollation, description, "ALTER ...");

    [Fact]
    public void BuildReportText_SuccessfulRun_MentionsSuccessAndEachStep()
    {
        var report = new MigrationReport(
            true,
            [new MigrationStepResult(Step("Collation von [dbo].[Orders].[Name] ändern"), true, null)],
            null,
            new VerificationResult([], []));
        var vm = new MigrationResultViewModel(report);

        var text = vm.BuildReportText();

        Assert.Contains(Strings.ReportSucceededWord, text);
        Assert.Contains($"[{Strings.ReportStepOk}]", text);
        Assert.Contains("Collation von [dbo].[Orders].[Name] ändern", text);
        Assert.Contains(Strings.ReportNoDiscrepancies, text);
    }

    [Fact]
    public void BuildReportText_FailedRun_IncludesFailureReasonAndFailedStepError()
    {
        var report = new MigrationReport(
            false,
            [new MigrationStepResult(Step("Index entfernen"), false, "boom")],
            "Schritt 'Index entfernen' fehlgeschlagen: boom",
            null);
        var vm = new MigrationResultViewModel(report);

        var text = vm.BuildReportText();

        Assert.Contains(Strings.ReportFailedWord, text);
        Assert.Contains("Schritt 'Index entfernen' fehlgeschlagen: boom", text);
        Assert.Contains($"[{Strings.ReportStepError}]", text);
        Assert.Contains("boom", text);
    }

    [Fact]
    public void BuildReportText_WithStructuralAndDataDiffs_ListsBoth()
    {
        var report = new MigrationReport(
            true,
            [],
            null,
            new VerificationResult(
                [new StructuralDiff("[dbo].[Orders]", "Index [IX_1] fehlt.")],
                [new DataDiff("[dbo].[Orders]", "Zeile 3 weicht ab.")]));
        var vm = new MigrationResultViewModel(report);

        var text = vm.BuildReportText();

        Assert.Contains(Strings.ReportDiscrepanciesFound, text);
        Assert.Contains($"[{Strings.ReportStructuralLabel}]", text);
        Assert.Contains("Index [IX_1] fehlt.", text);
        Assert.Contains($"[{Strings.ReportDataLabel}]", text);
        Assert.Contains("Zeile 3 weicht ab.", text);
    }

    [Fact]
    public void BuildReportText_DataVerificationSkipped_MentionsSkippedInsteadOfDiffs()
    {
        var report = new MigrationReport(
            true,
            [],
            null,
            new VerificationResult([], [], DataVerificationSkipped: true));
        var vm = new MigrationResultViewModel(report);

        var text = vm.BuildReportText();

        Assert.Contains($"[{Strings.ReportDataLabel}]", text);
        Assert.Contains(Strings.ReportDataVerificationSkipped, text);
    }

    [Fact]
    public void RestartCommand_RaisesRestartRequested()
    {
        var vm = new MigrationResultViewModel(new MigrationReport(true, [], null, null));
        var raised = false;
        vm.RestartRequested += (_, _) => raised = true;

        vm.RestartCommand.Execute(null);

        Assert.True(raised);
    }

    [Fact]
    public void ExportReportCommand_RaisesExportRequestedWithReportText()
    {
        var vm = new MigrationResultViewModel(new MigrationReport(true, [], null, null));
        string? exportedText = null;
        vm.ExportRequested += (_, text) => exportedText = text;

        vm.ExportReportCommand.Execute(null);

        Assert.NotNull(exportedText);
        Assert.Contains(Strings.ReportSucceededWord, exportedText);
    }
}
