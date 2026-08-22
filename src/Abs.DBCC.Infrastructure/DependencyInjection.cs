using Abs.DBCC.Application.Ports;
using Abs.DBCC.Infrastructure.Catalog;
using Abs.DBCC.Infrastructure.Migration;
using Abs.DBCC.Infrastructure.Snapshot;
using Abs.DBCC.Infrastructure.Sql;
using Abs.DBCC.Infrastructure.Verification;
using Microsoft.Extensions.DependencyInjection;

namespace Abs.DBCC.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ISqlScriptRunnerFactory, SqlScriptRunnerFactory>();
        services.AddSingleton<ICollationCatalogService, CollationCatalogService>();
        services.AddSingleton<IDatabaseInspectionService, DatabaseInspectionService>();
        services.AddSingleton<ISchemaSnapshotService, SchemaSnapshotService>();
        services.AddSingleton<IMigrationPlanBuilder, MigrationPlanBuilder>();
        services.AddSingleton<IMigrationOrchestrator, MigrationOrchestrator>();
        services.AddSingleton<IStructuralVerificationService, StructuralVerificationService>();
        services.AddSingleton<IDataVerificationService, DataVerificationService>();
        services.AddSingleton<IPreflightCheckService, PreflightCheckService>();

        return services;
    }
}
