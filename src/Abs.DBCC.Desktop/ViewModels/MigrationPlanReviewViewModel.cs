using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Migration;
using Abs.DBCC.Desktop.Localization;
using Abs.DBCC.Domain.Migration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;

namespace Abs.DBCC.Desktop.ViewModels;

public sealed record StepKindCount(MigrationStepKind Kind, int Count);

public partial class MigrationPlanReviewViewModel : ViewModelBase
{
    private readonly ISender _sender;
    private readonly ConnectionProfile _profile;

    public MigrationPlan Plan { get; }

    public IReadOnlyList<StepKindCount> StepCounts { get; }

    [ObservableProperty]
    public partial bool IsLoadingPreflight { get; set; }

    [ObservableProperty]
    public partial PreflightCheckResult? Preflight { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// Skips the before/after row-hash capture and comparison. Intended for a production run once the
    /// migration was already verified (including its data) against a backup or secondary system, where
    /// re-verifying adds cost but no safety. The structural check still always runs.
    /// </summary>
    [ObservableProperty]
    public partial bool SkipDataVerification { get; set; }

    public string AffectedTablesDisplay { get; }

    public bool HasOtherActiveConnections => Preflight is { OtherActiveSessionCount: > 0 };

    public string? OtherActiveConnectionsDisplay =>
        Preflight is null ? null : string.Format(Strings.OtherActiveConnectionsFormat, Preflight.OtherActiveSessionCount);

    public string? EstimatedAffectedRowsDisplay =>
        Preflight is null ? null : string.Format(Strings.EstimatedAffectedRowsFormat, Preflight.EstimatedAffectedRowCount);

    public string? TransactionLogDisplay =>
        Preflight is null ? null : string.Format(Strings.TransactionLogFormat,
            string.Format(Strings.LogFileSizeFormat, Preflight.LogFileSizeBytes / 1024.0 / 1024.0, Preflight.LogUsedPercent));

    public string? EstimatedVerificationMemoryDisplay =>
        Preflight is null ? null : string.Format(Strings.EstimatedVerificationMemoryFormat,
            FormatBytes(DataVerificationMemoryEstimator.EstimateBytes(Preflight.TotalRowCount)), Preflight.TotalRowCount);

    public string? AvailableMemoryDisplay =>
        Preflight is null ? null : string.Format(Strings.AvailableMemoryFormat,
            FormatBytes(Preflight.AvailableMemoryBytes), FormatBytes(Preflight.TotalMemoryBytes));

    public bool EstimatedMemoryExceedsAvailable =>
        Preflight is not null && DataVerificationMemoryEstimator.EstimateBytes(Preflight.TotalRowCount) > Preflight.AvailableMemoryBytes;

    public string ProcessOverviewStep2 => string.Format(Strings.ProcessOverviewStep2Format, Plan.Steps.Count);

    private static string FormatBytes(long bytes)
    {
        const double gb = 1024.0 * 1024 * 1024;
        const double mb = 1024.0 * 1024;
        return bytes >= gb ? $"{bytes / gb:F1} GB" : $"{bytes / mb:F0} MB";
    }

    public event EventHandler<(ConnectionProfile Profile, MigrationPlan Plan, bool SkipDataVerification)>? StartRequested;
    public event EventHandler? BackRequested;

    /// <summary>Raised with the generated T-SQL script; the view handles the actual save-file dialog.</summary>
    public event EventHandler<string>? ScriptExportRequested;

    public MigrationPlanReviewViewModel(ISender sender, ConnectionProfile profile, MigrationPlan plan)
    {
        _sender = sender;
        _profile = profile;
        Plan = plan;
        AffectedTablesDisplay = string.Format(Strings.AffectedTablesFormat, plan.AffectedTables.Count);
        StepCounts = plan.Steps
            .GroupBy(s => s.Kind)
            .Select(g => new StepKindCount(g.Key, g.Count()))
            .OrderByDescending(c => c.Count)
            .ToList();

        _ = LoadPreflightAsync();
    }

    partial void OnPreflightChanged(PreflightCheckResult? value)
    {
        OnPropertyChanged(nameof(HasOtherActiveConnections));
        OnPropertyChanged(nameof(OtherActiveConnectionsDisplay));
        OnPropertyChanged(nameof(EstimatedAffectedRowsDisplay));
        OnPropertyChanged(nameof(TransactionLogDisplay));
        OnPropertyChanged(nameof(EstimatedVerificationMemoryDisplay));
        OnPropertyChanged(nameof(AvailableMemoryDisplay));
        OnPropertyChanged(nameof(EstimatedMemoryExceedsAvailable));
    }

    [RelayCommand]
    private async Task LoadPreflightAsync()
    {
        IsLoadingPreflight = true;
        ErrorMessage = null;

        try
        {
            Preflight = await _sender.Send(new GetPreflightCheckQuery(_profile, Plan));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoadingPreflight = false;
        }
    }

    [RelayCommand]
    private void Start() => StartRequested?.Invoke(this, (_profile, Plan, SkipDataVerification));

    [RelayCommand]
    private void ExportScript() =>
        ScriptExportRequested?.Invoke(this, MigrationScriptGenerator.Generate(Plan, _profile.Database));

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);
}
