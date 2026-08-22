using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Migration;
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

    public string? LogFileSizeDisplay =>
        Preflight is null ? null : $"{Preflight.LogFileSizeBytes / 1024.0 / 1024.0:F1} MB ({Preflight.LogUsedPercent:F1}% belegt)";

    public event EventHandler<(ConnectionProfile Profile, MigrationPlan Plan)>? StartRequested;
    public event EventHandler? BackRequested;

    public MigrationPlanReviewViewModel(ISender sender, ConnectionProfile profile, MigrationPlan plan)
    {
        _sender = sender;
        _profile = profile;
        Plan = plan;
        StepCounts = plan.Steps
            .GroupBy(s => s.Kind)
            .Select(g => new StepKindCount(g.Key, g.Count()))
            .OrderByDescending(c => c.Count)
            .ToList();

        _ = LoadPreflightAsync();
    }

    partial void OnPreflightChanged(PreflightCheckResult? value) => OnPropertyChanged(nameof(LogFileSizeDisplay));

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
    private void Start() => StartRequested?.Invoke(this, (_profile, Plan));

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);
}
