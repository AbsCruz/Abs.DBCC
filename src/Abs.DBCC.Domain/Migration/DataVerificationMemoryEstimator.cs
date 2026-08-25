namespace Abs.DBCC.Domain.Migration;

/// <summary>
/// Rough estimate of peak managed memory for data verification, based on total row count alone (cheap to
/// get from catalog metadata without scanning tables). Each row is captured as one 64-char hex SHA-256
/// hash (see <see cref="RowHash"/>), ~150 bytes with object/list overhead; before- and after-migration
/// hash lists are both held at once while comparing, hence the factor of two. A ballpark, not an exact figure.
/// </summary>
public static class DataVerificationMemoryEstimator
{
    private const int BytesPerRowHash = 150;
    private const int SimultaneousCaptures = 2;

    public static long EstimateBytes(long totalRowCount) =>
        totalRowCount * BytesPerRowHash * SimultaneousCaptures;
}
