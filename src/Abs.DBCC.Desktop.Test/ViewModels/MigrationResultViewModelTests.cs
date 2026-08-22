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

        Assert.Contains("erfolgreich", text);
        Assert.Contains("[OK]", text);
        Assert.Contains("Collation von [dbo].[Orders].[Name] ändern", text);
        Assert.Contains("keine Abweichungen", text);
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

        Assert.Contains("fehlgeschlagen", text);
        Assert.Contains("Schritt 'Index entfernen' fehlgeschlagen: boom", text);
        Assert.Contains("[FEHLER]", text);
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

        Assert.Contains("Abweichungen gefunden", text);
        Assert.Contains("[Struktur]", text);
        Assert.Contains("Index [IX_1] fehlt.", text);
        Assert.Contains("[Daten]", text);
        Assert.Contains("Zeile 3 weicht ab.", text);
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
        Assert.Contains("erfolgreich", exportedText);
    }
}
