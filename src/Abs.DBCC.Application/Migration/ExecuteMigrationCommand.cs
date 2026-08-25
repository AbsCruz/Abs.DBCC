using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Migration;
using FluentValidation;
using MediatR;

namespace Abs.DBCC.Application.Migration;

public sealed record ExecuteMigrationCommand(
    ConnectionProfile Profile,
    MigrationPlan Plan,
    IProgress<MigrationStepResult>? Progress = null,
    IProgress<MigrationPhaseProgress>? PhaseProgress = null,
    bool SkipDataVerification = false) : IRequest<MigrationReport>;

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
        var phaseProgress = request.PhaseProgress;
        var tableCount = request.Plan.PreSnapshot.Tables.Count;
        var verifyData = !request.SkipDataVerification;

        IReadOnlyList<TableRowsSnapshot> rowsBefore = [];
        if (verifyData)
        {
            phaseProgress?.Report(new MigrationPhaseProgress(MigrationPhaseKind.CapturingRowsBefore, 0, tableCount));
            rowsBefore = await dataVerification.CaptureRowsAsync(
                request.Profile, request.Plan.PreSnapshot,
                TableProgress(phaseProgress, MigrationPhaseKind.CapturingRowsBefore), cancellationToken);
        }

        phaseProgress?.Report(new MigrationPhaseProgress(MigrationPhaseKind.ExecutingSteps, 0, request.Plan.Steps.Count));
        var report = await orchestrator.ExecuteAsync(request.Profile, request.Plan, request.Progress, cancellationToken);
        if (!report.Succeeded)
            return report;

        phaseProgress?.Report(new MigrationPhaseProgress(MigrationPhaseKind.VerifyingStructure, 0, 1));
        var structuralDiffs = await structuralVerification.VerifyAsync(
            request.Profile, request.Plan.PreSnapshot, request.Plan.TargetCollation, cancellationToken);

        if (!verifyData)
            return report with { Verification = new VerificationResult(structuralDiffs, [], DataVerificationSkipped: true) };

        phaseProgress?.Report(new MigrationPhaseProgress(MigrationPhaseKind.CapturingRowsAfter, 0, tableCount));
        var rowsAfter = await dataVerification.CaptureRowsAsync(
            request.Profile, request.Plan.PreSnapshot,
            TableProgress(phaseProgress, MigrationPhaseKind.CapturingRowsAfter), cancellationToken);

        phaseProgress?.Report(new MigrationPhaseProgress(MigrationPhaseKind.ComparingData, 0, 1));
        var dataDiffs = dataVerification.Compare(rowsBefore, rowsAfter);

        return report with { Verification = new VerificationResult(structuralDiffs, dataDiffs) };
    }

    private static IProgress<TableCaptureProgress>? TableProgress(IProgress<MigrationPhaseProgress>? phaseProgress, MigrationPhaseKind kind) =>
        phaseProgress is null
            ? null
            : new Progress<TableCaptureProgress>(p => phaseProgress.Report(new MigrationPhaseProgress(kind, p.Completed, p.Total, p.CurrentTableName)));
}
