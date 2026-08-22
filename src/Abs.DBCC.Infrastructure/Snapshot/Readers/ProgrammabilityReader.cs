using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Snapshot.Readers;

/// <summary>
/// Reads views, stored procedures, functions and triggers (sys.objects + sys.sql_modules), each
/// captured as its complete original CREATE ... statement text plus whether it is schema-bound.
/// </summary>
public sealed class ProgrammabilityReader
{
    private const string Query = """
        SELECT
            s.name AS SchemaName,
            o.name AS ObjectName,
            o.type AS ObjectTypeCode,
            m.definition AS Definition,
            OBJECTPROPERTY(o.object_id, 'IsSchemaBound') AS IsSchemaBound
        FROM sys.objects o
        JOIN sys.schemas s ON s.schema_id = o.schema_id
        JOIN sys.sql_modules m ON m.object_id = o.object_id
        WHERE o.type IN ('V', 'P', 'FN', 'IF', 'TF', 'TR') AND o.is_ms_shipped = 0
        ORDER BY s.name, o.name
        """;

    public async Task<List<ObjectDefinition>> ReadAsync(ISqlScriptRunner runner, CancellationToken ct)
    {
        var rows = await runner.ExecuteQueryAsync(Query, ct: ct);
        return rows.Select(MapRow).ToList();
    }

    private static ObjectDefinition MapRow(IReadOnlyDictionary<string, object?> row)
    {
        var kind = ((string)row["ObjectTypeCode"]!).Trim() switch
        {
            "V" => DatabaseObjectKind.View,
            "P" => DatabaseObjectKind.StoredProcedure,
            "FN" or "IF" or "TF" => DatabaseObjectKind.Function,
            "TR" => DatabaseObjectKind.Trigger,
            var other => throw new InvalidOperationException($"Unexpected sys.objects.type '{other}'.")
        };

        var objectRef = new ObjectRef((string)row["SchemaName"]!, (string)row["ObjectName"]!, kind);
        var isSchemaBound = row["IsSchemaBound"] is int i && i == 1;

        return new ObjectDefinition(objectRef, (string)row["Definition"]!, isSchemaBound);
    }
}
