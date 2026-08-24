using Abs.DBCC.Application.Connections;
using Abs.DBCC.Desktop.Localization;
using Abs.DBCC.Domain.Migration;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Abs.DBCC.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly Func<ConnectionSetupViewModel> _connectionSetupFactory;
    private readonly Func<ConnectionProfile, CollationOverviewViewModel> _collationOverviewFactory;
    private readonly Func<ConnectionProfile, TargetCollationPickerViewModel> _targetCollationPickerFactory;
    private readonly Func<ConnectionProfile, MigrationPlan, MigrationPlanReviewViewModel> _planReviewFactory;
    private readonly Func<ConnectionProfile, MigrationPlan, MigrationRunViewModel> _migrationRunFactory;

    [ObservableProperty]
    public partial ViewModelBase CurrentViewModel { get; set; }

    /// <summary>
    /// The connection currently in use, shown as a banner on every screen once set. Null while the
    /// user is still on the connection setup screen (nothing confirmed yet); it is deliberately left
    /// untouched when navigating to the migration result screen, so the banner still shows what the
    /// just-completed migration ran against.
    /// </summary>
    [ObservableProperty]
    public partial ConnectionProfile? CurrentProfile { get; set; }

    public string? ConnectionDisplay =>
        CurrentProfile is null ? null : string.Format(Strings.ConnectedToFormat, CurrentProfile.Database, CurrentProfile.Server);

    partial void OnCurrentProfileChanged(ConnectionProfile? value) => OnPropertyChanged(nameof(ConnectionDisplay));

    public MainViewModel(
        Func<ConnectionSetupViewModel> connectionSetupFactory,
        Func<ConnectionProfile, CollationOverviewViewModel> collationOverviewFactory,
        Func<ConnectionProfile, TargetCollationPickerViewModel> targetCollationPickerFactory,
        Func<ConnectionProfile, MigrationPlan, MigrationPlanReviewViewModel> planReviewFactory,
        Func<ConnectionProfile, MigrationPlan, MigrationRunViewModel> migrationRunFactory)
    {
        _connectionSetupFactory = connectionSetupFactory;
        _collationOverviewFactory = collationOverviewFactory;
        _targetCollationPickerFactory = targetCollationPickerFactory;
        _planReviewFactory = planReviewFactory;
        _migrationRunFactory = migrationRunFactory;

        CurrentViewModel = CreateConnectionSetup();
    }

    private ConnectionSetupViewModel CreateConnectionSetup()
    {
        CurrentProfile = null;
        var vm = _connectionSetupFactory();
        vm.ConnectionConfirmed += (_, profile) => CurrentViewModel = CreateCollationOverview(profile);
        return vm;
    }

    private CollationOverviewViewModel CreateCollationOverview(ConnectionProfile profile)
    {
        CurrentProfile = profile;
        var vm = _collationOverviewFactory(profile);
        vm.BackRequested += (_, _) => CurrentViewModel = CreateConnectionSetup();
        vm.ContinueRequested += (_, _) => CurrentViewModel = CreateTargetCollationPicker(profile);
        return vm;
    }

    private TargetCollationPickerViewModel CreateTargetCollationPicker(ConnectionProfile profile)
    {
        CurrentProfile = profile;
        var vm = _targetCollationPickerFactory(profile);
        vm.BackRequested += (_, _) => CurrentViewModel = CreateCollationOverview(profile);
        vm.PlanBuilt += (_, args) => CurrentViewModel = CreatePlanReview(args.Profile, args.Plan);
        return vm;
    }

    private MigrationPlanReviewViewModel CreatePlanReview(ConnectionProfile profile, MigrationPlan plan)
    {
        CurrentProfile = profile;
        var vm = _planReviewFactory(profile, plan);
        vm.BackRequested += (_, _) => CurrentViewModel = CreateTargetCollationPicker(profile);
        vm.StartRequested += (_, args) => CurrentViewModel = CreateMigrationRun(args.Profile, args.Plan);
        return vm;
    }

    private MigrationRunViewModel CreateMigrationRun(ConnectionProfile profile, MigrationPlan plan)
    {
        CurrentProfile = profile;
        var vm = _migrationRunFactory(profile, plan);
        vm.Completed += (_, report) => CurrentViewModel = CreateMigrationResult(report);
        vm.CancelledAcknowledged += (_, _) => CurrentViewModel = CreateConnectionSetup();
        vm.UnexpectedErrorAcknowledged += (_, _) => CurrentViewModel = CreateConnectionSetup();
        vm.Start();
        return vm;
    }

    private MigrationResultViewModel CreateMigrationResult(MigrationReport report)
    {
        var vm = new MigrationResultViewModel(report);
        vm.RestartRequested += (_, _) => CurrentViewModel = CreateConnectionSetup();
        return vm;
    }
}
