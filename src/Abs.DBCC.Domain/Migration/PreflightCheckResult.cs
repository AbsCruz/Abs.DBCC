namespace Abs.DBCC.Domain.Migration;

public sealed record PreflightCheckResult(
    int OtherActiveSessionCount,
    long EstimatedAffectedRowCount,
    long TotalRowCount,
    long LogFileSizeBytes,
    double LogUsedPercent,
    long AvailableMemoryBytes,
    long TotalMemoryBytes);
