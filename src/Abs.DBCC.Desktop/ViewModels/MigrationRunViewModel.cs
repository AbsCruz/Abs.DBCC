using System.Collections.ObjectModel;
using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Migration;
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

    public int TotalStepCount { get; }

    public ObservableCollection<MigrationStepResult> StepResults { get; } = [];

    public event EventHandler<MigrationReport>? Completed;
    public event EventHandler? CancelledAcknowledged;
    public event EventHandler? UnexpectedErrorAcknowledged;

    public MigrationRunViewModel(ISender sender, ConnectionProfile profile, MigrationPlan plan)
    {
        _sender = sender;
        _profile = profile;
        _plan = plan;
        TotalStepCount = plan.Steps.Count;

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

    private async Task RunAsync()
    {
        var progress = new Progress<MigrationStepResult>(result =>
        {
            StepResults.Add(result);
            CompletedStepCount++;
        });

        try
        {
            var report = await _sender.Send(new ExecuteMigrationCommand(_profile, _plan, progress), _cancellationTokenSource.Token);
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
}
