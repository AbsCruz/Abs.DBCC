using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Snapshot.Readers;

public sealed class SynonymReader
{
    private const string Query = """
        SELECT s.name AS SchemaName, syn.name AS SynonymName, syn.base_object_name AS BaseObjectName
        FROM sys.synonyms syn
        JOIN sys.schemas s ON s.schema_id = syn.schema_id
        ORDER BY s.name, syn.name
        """;

    public async Task<List<SynonymSnapshot>> ReadAsync(ISqlScriptRunner runner, CancellationToken ct)
    {
        var rows = await runner.ExecuteQueryAsync(Query, ct: ct);
        return rows.Select(row => new SynonymSnapshot(
            new ObjectRef((string)row["SchemaName"]!, (string)row["SynonymName"]!, DatabaseObjectKind.Synonym),
            (string)row["BaseObjectName"]!)).ToList();
    }
}
