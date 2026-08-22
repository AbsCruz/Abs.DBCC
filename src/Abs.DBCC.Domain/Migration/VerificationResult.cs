namespace Abs.DBCC.Domain.Migration;

public sealed record StructuralDiff(string ObjectDescription, string Details);

public sealed record DataDiff(string TableDescription, string Details);

public sealed record VerificationResult(
    IReadOnlyList<StructuralDiff> StructuralDiffs,
    IReadOnlyList<DataDiff> DataDiffs)
{
    public bool IsSuccess => StructuralDiffs.Count == 0 && DataDiffs.Count == 0;
}
