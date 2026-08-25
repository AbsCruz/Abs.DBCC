using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.Domain.Snapshot;

namespace Abs.DBCC.Infrastructure.Migration;

public sealed class PreflightCheckService(
    ISqlScriptRunnerFactory runnerFactory, ISystemMemoryInfoProvider systemMemory) : IPreflightCheckService
{
    private const string ActiveSessionCountQuery = """
        SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE database_id = DB_ID() AND session_id <> @@SPID
        """;

    // sys.partitions.rows is metadata SQL Server already maintains, so this avoids a full table scan.
    private const string TableRowCountsQuery = """
        SELECT s.name AS SchemaName, t.name AS TableName, SUM(p.rows) AS [RowCount]
        FROM sys.tables t
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
        WHERE t.is_ms_shipped = 0
        GROUP BY s.name, t.name
        """;

    // Converting large character columns in place is fully logged; surfacing current log size/usage
    // lets the UI warn before a migration that could grow the log significantly.
    private const string LogSpaceUsageQuery = """
        SELECT total_log_size_in_bytes, used_log_space_in_percent FROM sys.dm_db_log_space_usage;
        """;

    public async Task<PreflightCheckResult> CheckAsync(ConnectionProfile profile, Domain.Migration.MigrationPlan plan, CancellationToken ct = default)
    {
        await using var runner = await runnerFactory.CreateAsync(profile, ct);

        var sessionCount = await runner.ExecuteScalarAsync<int>(ActiveSessionCountQuery, ct: ct);

        var affectedTables = plan.AffectedTables.ToHashSet();
        var rows = await runner.ExecuteQueryAsync(TableRowCountsQuery, ct: ct);
        var affectedRowCount = rows
            .Where(row => affectedTables.Contains(new ObjectRef((string)row["SchemaName"]!, (string)row["TableName"]!, DatabaseObjectKind.Table)))
            .Sum(row => Convert.ToInt64(row["RowCount"]));

        // Data verification hashes every table in the database, not just the affected ones (see
        // DataVerificationService), so the memory estimate needs the total row count across all of them.
        var totalRowCount = rows.Sum(row => Convert.ToInt64(row["RowCount"]));

        var logRows = await runner.ExecuteQueryAsync(LogSpaceUsageQuery, ct: ct);
        var logRow = logRows.SingleOrDefault();
        var logFileSizeBytes = logRow is null ? 0L : Convert.ToInt64(logRow["total_log_size_in_bytes"]);
        var logUsedPercent = logRow is null ? 0d : Convert.ToDouble(logRow["used_log_space_in_percent"]);

        var (availableMemoryBytes, totalMemoryBytes) = systemMemory.GetPhysicalMemory();

        return new PreflightCheckResult(
            sessionCount, affectedRowCount, totalRowCount, logFileSizeBytes, logUsedPercent, availableMemoryBytes, totalMemoryBytes);
    }
}
