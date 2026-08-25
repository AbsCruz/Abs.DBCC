namespace Abs.DBCC.Domain.Migration;

/// <summary>
/// Rough estimate of the peak managed memory the data-verification phases will need, based purely on
/// total row count - the one number that's cheap to obtain from catalog metadata without scanning any
/// table (see the preflight check's row-count query).
///
/// Each row is captured as one hex-encoded SHA-256 hash rather than its full column values (see
/// <see cref="RowHash"/>): a 64-character .NET string, roughly 150 bytes including object and list-slot
/// overhead. The before- and after-migration hash lists are both held in memory at the same time while
/// comparing, so the estimate accounts for two captures. This is a coarse approximation meant to give the
/// user a ballpark before starting a long-running migration, not an exact prediction - actual overhead
/// varies with .NET version, table count, and GC behavior.
/// </summary>
public static class DataVerificationMemoryEstimator
{
    private const int BytesPerRowHash = 150;
    private const int SimultaneousCaptures = 2;

    public static long EstimateBytes(long totalRowCount) =>
        totalRowCount * BytesPerRowHash * SimultaneousCaptures;
}
