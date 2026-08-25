using System.Collections.ObjectModel;
using System.Diagnostics;
using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Migration;
using Abs.DBCC.Desktop.Localization;
using Abs.DBCC.Domain.Migration;
using Avalonia.Threading;
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
    private readonly Stopwatch _stopwatch = new();

    /// <summary>
    /// A plain System.Timers.Timer, not Avalonia's DispatcherTimer: its Elapsed event fires on a
    /// thread-pool thread, so the tick must marshal via Dispatcher.UIThread.Post. Posting is harmless
    /// in unit tests too, where nothing pumps the UI dispatcher.
    /// </summary>
    private readonly System.Timers.Timer _elapsedTimeTimer = new(TimeSpan.FromSeconds(1)) { AutoReset = true };

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

    [ObservableProperty]
    public partial TimeSpan ElapsedTime { get; set; }

    public string ElapsedTimeDisplay => ElapsedTime.ToString(@"hh\:mm\:ss");

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

        _elapsedTimeTimer.Elapsed += (_, _) => Dispatcher.UIThread.Post(() => ElapsedTime = _stopwatch.Elapsed);
        _stopwatch.Start();
        _elapsedTimeTimer.Start();
    }

    /// <summary>
    /// Kicks off the migration. Deliberately not called from the constructor: if the underlying work
    /// happened to complete synchronously (e.g. an immediate validation failure, or a fake/completed
    /// task in a test), firing <see cref="Completed"/> from within the constructor would race the
    /// caller's own subscription to it, wired up only after the constructor returns - see MainViewModel
    /// (Completed/CancelledAcknowledged/UnexpectedErrorAcknowledged are all subscribed there). Callers
    /// must wire up their event handlers first, then invoke this.
    /// </summary>
    public void Start() => _ = RunAsync();

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

    partial void OnElapsedTimeChanged(TimeSpan value) => OnPropertyChanged(nameof(ElapsedTimeDisplay));

    /// <summary>Stops the ticking timer and takes one final, precise reading - the last tick can be up to a second stale.</summary>
    private void StopElapsedTimer()
    {
        _elapsedTimeTimer.Stop();
        _stopwatch.Stop();
        ElapsedTime = _stopwatch.Elapsed;
    }

    private async Task RunAsync()
    {
        // A direct callback, not System.Threading.Progress<T>: Progress<T> marshals via
        // SynchronizationContext.Post, which queues updates and can lag behind when several reports fire
        // in quick succession. Nothing here uses ConfigureAwait(false), so every await already resumes on
        // RunAsync's original context, making that marshalling unnecessary.
        var stepProgress = new DirectProgress<MigrationStepResult>(result =>
        {
            // Newest first for a live viewer; the exported report keeps MigrationReport.StepResults'
            // chronological order.
            StepResults.Insert(0, result);
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
            StopElapsedTimer();
            Completed?.Invoke(this, report);
        }
        catch (OperationCanceledException)
        {
            // Cancellation fails the current step cleanly and rolls it back like any other step failure
            // (see MigrationOrchestrator); no MigrationReport exists for a run that never reached the
            // handler's return.
            IsRunning = false;
            StopElapsedTimer();
            WasCancelled = true;
        }
        catch (Exception ex)
        {
            // Anything escaping ExecuteMigrationCommand itself (e.g. the connection dropping before a
            // MigrationReport exists) must still leave the ViewModel in a defined terminal state instead
            // of hanging with IsRunning still true.
            IsRunning = false;
            StopElapsedTimer();
            HasUnexpectedError = true;
            UnexpectedErrorMessage = ex.Message;
        }
    }

    private sealed class DirectProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
