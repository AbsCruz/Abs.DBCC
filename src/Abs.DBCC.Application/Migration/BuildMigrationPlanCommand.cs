using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Migration;
using FluentValidation;
using MediatR;

namespace Abs.DBCC.Application.Migration;

public sealed record BuildMigrationPlanCommand(
    ConnectionProfile Profile,
    SqlCollationName TargetCollation,
    bool UpdateDatabaseDefaultCollation = true,
    IReadOnlySet<ColumnRef>? ExcludedColumns = null) : IRequest<MigrationPlan>;

public sealed class BuildMigrationPlanCommandValidator : AbstractValidator<BuildMigrationPlanCommand>
{
    public BuildMigrationPlanCommandValidator()
    {
        RuleFor(c => c.Profile).SetValidator(new ConnectionProfileValidator());
        RuleFor(c => c.TargetCollation).NotNull();
    }
}

public sealed class BuildMigrationPlanCommandHandler(
    ISchemaSnapshotService snapshotService,
    IMigrationPlanBuilder planBuilder) : IRequestHandler<BuildMigrationPlanCommand, MigrationPlan>
{
    public async Task<MigrationPlan> Handle(BuildMigrationPlanCommand request, CancellationToken cancellationToken)
    {
        var snapshot = await snapshotService.CaptureAsync(request.Profile, cancellationToken);
        return planBuilder.Build(snapshot, request.TargetCollation, request.UpdateDatabaseDefaultCollation, request.ExcludedColumns);
    }
}
