using Abs.DBCC.Application.Collations;
using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Migration;
using Abs.DBCC.Domain.Collation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using MigrationPlan = Abs.DBCC.Domain.Migration.MigrationPlan;

namespace Abs.DBCC.Desktop.ViewModels;

public partial class TargetCollationPickerViewModel : ViewModelBase
{
    private readonly ISender _sender;
    private readonly ConnectionProfile _profile;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsBuildingPlan { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<CollationInfo> Collations { get; set; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildPlanCommand))]
    public partial CollationInfo? SelectedCollation { get; set; }

    [ObservableProperty]
    public partial bool UpdateDatabaseDefaultCollation { get; set; } = true;

    public IEnumerable<CollationInfo> FilteredCollations =>
        string.IsNullOrWhiteSpace(SearchText)
            ? Collations
            : Collations.Where(c => c.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

    public event EventHandler<(ConnectionProfile Profile, MigrationPlan Plan)>? PlanBuilt;
    public event EventHandler? BackRequested;

    public TargetCollationPickerViewModel(ISender sender, ConnectionProfile profile)
    {
        _sender = sender;
        _profile = profile;
        _ = LoadAsync();
    }

    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(FilteredCollations));

    partial void OnCollationsChanged(IReadOnlyList<CollationInfo> value) => OnPropertyChanged(nameof(FilteredCollations));

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            Collations = await _sender.Send(new GetAvailableCollationsQuery(_profile));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanBuildPlan => SelectedCollation is not null;

    [RelayCommand(CanExecute = nameof(CanBuildPlan))]
    private async Task BuildPlanAsync()
    {
        if (SelectedCollation is null)
            return;

        IsBuildingPlan = true;
        ErrorMessage = null;

        try
        {
            var plan = await _sender.Send(new BuildMigrationPlanCommand(
                _profile, new SqlCollationName(SelectedCollation.Name), UpdateDatabaseDefaultCollation));
            PlanBuilt?.Invoke(this, (_profile, plan));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBuildingPlan = false;
        }
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);
}
