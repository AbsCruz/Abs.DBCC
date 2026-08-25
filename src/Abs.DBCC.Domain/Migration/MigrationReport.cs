namespace Abs.DBCC.Domain.Migration;

public sealed record MigrationStepResult(MigrationStep Step, bool Succeeded, string? Error, DateTime Timestamp);

public sealed record MigrationReport(
    bool Succeeded,
    IReadOnlyList<MigrationStepResult> StepResults,
    string? FailureReason,
    VerificationResult? Verification);
