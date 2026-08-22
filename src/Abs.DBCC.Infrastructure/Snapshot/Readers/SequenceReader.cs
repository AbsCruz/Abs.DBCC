using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Snapshot.Readers;

public sealed class SequenceReader
{
    private const string Query = """
        SELECT
            s.name AS SchemaName,
            seq.name AS SequenceName,
            ty.name AS DataType,
            CAST(seq.start_value AS nvarchar(50)) AS StartValue,
            CAST(seq.increment AS nvarchar(50)) AS Increment,
            CAST(seq.minimum_value AS nvarchar(50)) AS MinValue,
            CAST(seq.maximum_value AS nvarchar(50)) AS MaxValue,
            seq.is_cycling AS IsCycling,
            seq.cache_size AS CacheSize
        FROM sys.sequences seq
        JOIN sys.schemas s ON s.schema_id = seq.schema_id
        JOIN sys.types ty ON ty.user_type_id = seq.user_type_id
        WHERE seq.is_ms_shipped = 0
        ORDER BY s.name, seq.name
        """;

    public async Task<List<SequenceSnapshot>> ReadAsync(ISqlScriptRunner runner, CancellationToken ct)
    {
        var rows = await runner.ExecuteQueryAsync(Query, ct: ct);
        return rows.Select(row => new SequenceSnapshot(
            new ObjectRef((string)row["SchemaName"]!, (string)row["SequenceName"]!, DatabaseObjectKind.Sequence),
            (string)row["DataType"]!,
            (string)row["StartValue"]!,
            (string)row["Increment"]!,
            row["MinValue"] as string,
            row["MaxValue"] as string,
            (bool)row["IsCycling"]!,
            Convert.ToInt64(row["CacheSize"] ?? 0L))).ToList();
    }
}
