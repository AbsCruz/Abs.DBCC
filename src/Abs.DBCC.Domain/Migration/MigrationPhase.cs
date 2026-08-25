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
/// Reports which phase a migration run is currently in, and how far it has progressed within it.
/// <paramref name="CurrentTableName"/> is set only for the table-by-table capture phases, so the UI can
/// show which table is currently being read - useful since a single large table can dominate the whole
/// phase's duration, during which the completed/total counters alone don't move.
/// </summary>
public sealed record MigrationPhaseProgress(MigrationPhaseKind Kind, int Completed, int Total, string? CurrentTableName = null);

/// <summary>Which of a snapshot's tables <see cref="RowHash"/> capture is currently reading.</summary>
public sealed record TableCaptureProgress(int Completed, int Total, string CurrentTableName);
