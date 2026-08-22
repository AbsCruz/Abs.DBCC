using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Snapshot.Readers;

/// <summary>
/// Reads sys.database_permissions for class 0 (database-level), class 1 (object/column-level) and
/// class 3 (schema-level) - covering the vast majority of real-world GRANT/DENY usage. Indexes and
/// constraints are not grantable securables in SQL Server, so they never appear here (only their
/// extended properties can - see ExtendedPropertyReader). More exotic securable classes (assemblies,
/// certificates, symmetric/asymmetric keys, service broker objects, ...) remain a known scope
/// limitation - see docs/BekannteEinschraenkungen.md.
/// </summary>
public sealed class PermissionReader
{
    private const string DatabaseLevelQuery = """
        SELECT pr.name AS GranteePrincipal, dp.permission_name AS PermissionName, dp.state_desc AS State
        FROM sys.database_permissions dp
        JOIN sys.database_principals pr ON pr.principal_id = dp.grantee_principal_id
        WHERE dp.class = 0
        ORDER BY dp.permission_name
        """;

    private const string SchemaLevelQuery = """
        SELECT pr.name AS GranteePrincipal, dp.permission_name AS PermissionName, dp.state_desc AS State, s.name AS SchemaName
        FROM sys.database_permissions dp
        JOIN sys.database_principals pr ON pr.principal_id = dp.grantee_principal_id
        JOIN sys.schemas s ON s.schema_id = dp.major_id
        WHERE dp.class = 3
        ORDER BY s.name, dp.permission_name
        """;

    private const string ObjectLevelQuery = """
        SELECT
            pr.name AS GranteePrincipal,
            dp.permission_name AS PermissionName,
            dp.state_desc AS State,
            s.name AS SchemaName,
            o.name AS ObjectName,
            o.type AS ObjectTypeCode,
            c.name AS ColumnName
        FROM sys.database_permissions dp
        JOIN sys.database_principals pr ON pr.principal_id = dp.grantee_principal_id
        JOIN sys.objects o ON o.object_id = dp.major_id
        JOIN sys.schemas s ON s.schema_id = o.schema_id
        LEFT JOIN sys.columns c ON c.object_id = dp.major_id AND c.column_id = dp.minor_id AND dp.minor_id > 0
        WHERE dp.class = 1 AND o.is_ms_shipped = 0
        ORDER BY s.name, o.name, dp.permission_name
        """;

    public async Task<List<PermissionSnapshot>> ReadAsync(ISqlScriptRunner runner, CancellationToken ct)
    {
        var result = new List<PermissionSnapshot>();

        var databaseRows = await runner.ExecuteQueryAsync(DatabaseLevelQuery, ct: ct);
        result.AddRange(databaseRows.Select(row => new PermissionSnapshot(
            (string)row["GranteePrincipal"]!, (string)row["PermissionName"]!, (string)row["State"]!, null, null)));

        var schemaRows = await runner.ExecuteQueryAsync(SchemaLevelQuery, ct: ct);
        result.AddRange(schemaRows.Select(row => new PermissionSnapshot(
            (string)row["GranteePrincipal"]!, (string)row["PermissionName"]!, (string)row["State"]!, null, null, (string)row["SchemaName"]!)));

        var objectRows = await runner.ExecuteQueryAsync(ObjectLevelQuery, ct: ct);
        foreach (var row in objectRows)
        {
            var kind = ObjectTypeMapper.TryMap((string)row["ObjectTypeCode"]!);
            if (kind is null)
                continue; // exotic/unmapped object type (e.g. queue, XML schema collection) - out of scope

            var objectRef = new ObjectRef((string)row["SchemaName"]!, (string)row["ObjectName"]!, kind.Value);
            result.Add(new PermissionSnapshot(
                (string)row["GranteePrincipal"]!, (string)row["PermissionName"]!, (string)row["State"]!,
                objectRef, row["ColumnName"] as string));
        }

        return result;
    }
}
