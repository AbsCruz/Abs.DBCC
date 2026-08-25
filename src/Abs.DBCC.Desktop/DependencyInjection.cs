using Abs.DBCC.Application.Connections;
using Abs.DBCC.Desktop.ViewModels;
using Abs.DBCC.Domain.Migration;
using Microsoft.Extensions.DependencyInjection;

namespace Abs.DBCC.Desktop;

public static class DependencyInjection
{
    public static IServiceCollection AddDesktop(this IServiceCollection services)
    {
        services.AddTransient<ConnectionSetupViewModel>();
        services.AddSingleton<Func<ConnectionSetupViewModel>>(sp => sp.GetRequiredService<ConnectionSetupViewModel>);
        services.AddSingleton<Func<ConnectionProfile, CollationOverviewViewModel>>(
            sp => profile => ActivatorUtilities.CreateInstance<CollationOverviewViewModel>(sp, profile));
        services.AddSingleton<Func<ConnectionProfile, TargetCollationPickerViewModel>>(
            sp => profile => ActivatorUtilities.CreateInstance<TargetCollationPickerViewModel>(sp, profile));
        services.AddSingleton<Func<ConnectionProfile, MigrationPlan, MigrationPlanReviewViewModel>>(
            sp => (profile, plan) => ActivatorUtilities.CreateInstance<MigrationPlanReviewViewModel>(sp, profile, plan));
        services.AddSingleton<Func<ConnectionProfile, MigrationPlan, bool, MigrationRunViewModel>>(
            sp => (profile, plan, skipDataVerification) =>
                ActivatorUtilities.CreateInstance<MigrationRunViewModel>(sp, profile, plan, skipDataVerification));

        services.AddSingleton<MainViewModel>();

        return services;
    }
}
