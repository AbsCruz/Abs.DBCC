using System.Collections.ObjectModel;
using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Migration;
using Abs.DBCC.Desktop.Localization;
using Abs.DBCC.Domain.Migration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;

namespace Abs.DBCC.Desktop.ViewModels;

public partial class MigrationRunViewModel : ViewModelBase
{
    private readonly ISender _sender;
    private readonly ConnectionProfile _profile;
    private readonly MigrationPlan _plan;
    private readonly bool _skipDataVerification;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    [ObservableProperty]
    public partial bool IsRunning { get; set; } = true;

    [ObservableProperty]
    public partial bool IsCancelling { get; set; }

    [ObservableProperty]
    public partial bool WasCancelled { get; set; }

    [ObservableProperty]
    public partial bool HasUnexpectedError { get; set; }

    [ObservableProperty]
    public partial string? UnexpectedErrorMessage { get; set; }

    [ObservableProperty]
    public partial int CompletedStepCount { get; set; }

    [ObservableProperty]
    public partial MigrationPhaseKind Phase { get; set; } = MigrationPhaseKind.CapturingRowsBefore;

    [ObservableProperty]
    public partial int PhaseCompleted { get; set; }

    [ObservableProperty]
    public partial int PhaseTotal { get; set; } = 1;

    [ObservableProperty]
    public partial string? CurrentTableName { get; set; }

    public int TotalStepCount { get; }

    /// <summary>False for the single-shot, indeterminate phases (structure check, comparison) that have no meaningful count.</summary>
    public bool PhaseHasCount => Phase is MigrationPhaseKind.CapturingRowsBefore or MigrationPhaseKind.ExecutingSteps or MigrationPhaseKind.CapturingRowsAfter;

    public bool ShowPhaseCount => IsRunning && PhaseHasCount;

    public bool ShowCurrentTableName =>
        IsRunning && Phase is MigrationPhaseKind.CapturingRowsBefore or MigrationPhaseKind.CapturingRowsAfter
        && !string.IsNullOrEmpty(CurrentTableName);

    public string PhaseDescription => Phase switch
    {
        MigrationPhaseKind.CapturingRowsBefore => Strings.PhaseCapturingRowsBefore,
        MigrationPhaseKind.ExecutingSteps => Strings.PhaseExecutingSteps,
        MigrationPhaseKind.VerifyingStructure => Strings.PhaseVerifyingStructure,
        MigrationPhaseKind.CapturingRowsAfter => Strings.PhaseCapturingRowsAfter,
        MigrationPhaseKind.ComparingData => Strings.PhaseComparingData,
        _ => string.Empty
    };

    public string PhaseUnitSuffix => Phase == MigrationPhaseKind.ExecutingSteps ? Strings.StepsSuffix : Strings.TablesSuffix;

    public ObservableCollection<MigrationStepResult> StepResults { get; } = [];

    public event EventHandler<MigrationReport>? Completed;
    public event EventHandler? CancelledAcknowledged;
    public event EventHandler? UnexpectedErrorAcknowledged;

    public MigrationRunViewModel(ISender sender, ConnectionProfile profile, MigrationPlan plan, bool skipDataVerification = false)
    {
        _sender = sender;
        _profile = profile;
        _plan = plan;
        _skipDataVerification = skipDataVerification;
        TotalStepCount = plan.Steps.Count;
        PhaseTotal = Math.Max(plan.PreSnapshot.Tables.Count, 1);

        _ = RunAsync();
    }

    private bool CanCancel => IsRunning && !IsCancelling;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        IsCancelling = true;
        _cancellationTokenSource.Cancel();
    }

    [RelayCommand]
    private void AcknowledgeCancelled() => CancelledAcknowledged?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void AcknowledgeUnexpectedError() => UnexpectedErrorAcknowledged?.Invoke(this, EventArgs.Empty);

    partial void OnPhaseChanged(MigrationPhaseKind value)
    {
        OnPropertyChanged(nameof(PhaseDescription));
        OnPropertyChanged(nameof(PhaseHasCount));
        OnPropertyChanged(nameof(PhaseUnitSuffix));
        OnPropertyChanged(nameof(ShowPhaseCount));
        OnPropertyChanged(nameof(ShowCurrentTableName));
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPhaseCount));
        OnPropertyChanged(nameof(ShowCurrentTableName));
    }

    partial void OnCurrentTableNameChanged(string? value) => OnPropertyChanged(nameof(ShowCurrentTableName));

    private async Task RunAsync()
    {
        // A direct callback, not System.Threading.Progress<T>: Progress<T> always marshals via
        // SynchronizationContext.Post, which queues the update instead of applying it immediately - with
        // several reports firing in quick succession (e.g. the CapturingRowsBefore phase's report
        // immediately followed by ExecutingSteps once capture finishes), the queued posts can still be
        // pending when the UI is inspected, so the displayed phase lags behind or appears stuck. Nothing
        // in this call chain uses ConfigureAwait(false), so every await here already resumes on the same
        // context RunAsync started on - these reports arrive already on the right thread, making the
        // extra marshalling both unnecessary and the source of the lag.
        var stepProgress = new DirectProgress<MigrationStepResult>(result =>
        {
            StepResults.Add(result);
            CompletedStepCount++;

            if (Phase == MigrationPhaseKind.ExecutingSteps)
                PhaseCompleted = CompletedStepCount;
        });

        var phaseProgress = new DirectProgress<MigrationPhaseProgress>(p =>
        {
            Phase = p.Kind;
            PhaseCompleted = p.Completed;
            PhaseTotal = Math.Max(p.Total, 1);
            CurrentTableName = p.CurrentTableName;
        });

        try
        {
            var report = await _sender.Send(
                new ExecuteMigrationCommand(_profile, _plan, stepProgress, phaseProgress, _skipDataVerification),
                _cancellationTokenSource.Token);
            IsRunning = false;
            Completed?.Invoke(this, report);
        }
        catch (OperationCanceledException)
        {
            // Cancellation stops the current step (which fails cleanly and rolls back, exactly like any
            // other step failure - see MigrationOrchestrator) rather than corrupting or partially
            // applying it; there is no MigrationReport for a run that never reached its handler's return.
            IsRunning = false;
            WasCancelled = true;
        }
        catch (Exception ex)
        {
            // Anything that escapes ExecuteMigrationCommand itself (e.g. the connection dropping before
            // the orchestrator can even produce a MigrationReport - see MigrationOrchestrator.TryRollbackAsync
            // for the case where it drops mid-step) must still leave this ViewModel in a defined terminal
            // state rather than hanging forever with IsRunning still true and an unobserved exception.
            IsRunning = false;
            HasUnexpectedError = true;
            UnexpectedErrorMessage = ex.Message;
        }
    }

    private sealed class DirectProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
