namespace Abs.DBCC.Domain.Migration;

public sealed record PreflightCheckResult(
    int OtherActiveSessionCount,
    long EstimatedAffectedRowCount,
    long LogFileSizeBytes,
    double LogUsedPercent);
