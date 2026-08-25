namespace Abs.DBCC.Domain.Migration;

/// <summary>The distinct phases a migration run passes through, in order.</summary>
public enum MigrationPhaseKind
{
    CapturingRowsBefore,
    ExecutingSteps,
    VerifyingStructure,
    CapturingRowsAfter,
    ComparingData
}

/// <summary>
/// Reports which phase a migration run is in and its progress. <paramref name="CurrentTableName"/> is set
/// only for the table-by-table capture phases, so the UI can show which table is being read even while a
/// large table holds the completed/total counters still.
/// </summary>
public sealed record MigrationPhaseProgress(MigrationPhaseKind Kind, int Completed, int Total, string? CurrentTableName = null);

/// <summary>Which of a snapshot's tables <see cref="RowHash"/> capture is currently reading.</summary>
public sealed record TableCaptureProgress(int Completed, int Total, string CurrentTableName);
