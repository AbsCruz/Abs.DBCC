using System.Security.Cryptography;
using System.Text;

namespace Abs.DBCC.Domain.Migration;

/// <summary>
/// Computes a canonical, order- and representation-independent hash for one row, so large tables can be
/// verified before/after a migration without holding every row's full content in memory at once.
/// </summary>
public static class RowHash
{
    public static string Compute(IReadOnlyDictionary<string, object?> row)
    {
        var canonical = string.Join("|", row
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key}={FormatValue(kv.Value)}"));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "<null>",
        byte[] bytes => Convert.ToHexString(bytes),
        _ => value.ToString() ?? "<null>"
    };
}
