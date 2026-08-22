using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Migration;
using FluentValidation;
using MediatR;

namespace Abs.DBCC.Application.Migration;

public sealed record ExecuteMigrationCommand(
    ConnectionProfile Profile,
    MigrationPlan Plan,
    IProgress<MigrationStepResult>? Progress = null) : IRequest<MigrationReport>;

public sealed class ExecuteMigrationCommandValidator : AbstractValidator<ExecuteMigrationCommand>
{
    public ExecuteMigrationCommandValidator()
    {
        RuleFor(c => c.Profile).SetValidator(new ConnectionProfileValidator());
        RuleFor(c => c.Plan).NotNull();
    }
}

public sealed class ExecuteMigrationCommandHandler(
    IMigrationOrchestrator orchestrator,
    IStructuralVerificationService structuralVerification,
    IDataVerificationService dataVerification) : IRequestHandler<ExecuteMigrationCommand, MigrationReport>
{
    public async Task<MigrationReport> Handle(ExecuteMigrationCommand request, CancellationToken cancellationToken)
    {
        var rowsBefore = await dataVerification.CaptureRowsAsync(request.Profile, request.Plan.PreSnapshot, cancellationToken);

        var report = await orchestrator.ExecuteAsync(request.Profile, request.Plan, request.Progress, cancellationToken);
        if (!report.Succeeded)
            return report;

        var structuralDiffs = await structuralVerification.VerifyAsync(
            request.Profile, request.Plan.PreSnapshot, request.Plan.TargetCollation, cancellationToken);
        var rowsAfter = await dataVerification.CaptureRowsAsync(request.Profile, request.Plan.PreSnapshot, cancellationToken);
        var dataDiffs = dataVerification.Compare(rowsBefore, rowsAfter);

        return report with { Verification = new VerificationResult(structuralDiffs, dataDiffs) };
    }
}
