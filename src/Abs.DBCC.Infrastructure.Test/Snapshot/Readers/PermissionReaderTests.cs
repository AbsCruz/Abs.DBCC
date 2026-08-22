using Abs.DBCC.Infrastructure.Snapshot.Readers;
using Abs.DBCC.TestCommon.Fakes;

namespace Abs.DBCC.Infrastructure.Test.Snapshot.Readers;

public class PermissionReaderTests
{
    [Fact]
    public async Task ReadAsync_MapsDatabaseLevelPermission()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult(
        [
            new Dictionary<string, object?> { ["GranteePrincipal"] = "app_user", ["PermissionName"] = "CONNECT", ["State"] = "GRANT" }
        ]);
        runner.EnqueueQueryResult([]); // schema-level
        runner.EnqueueQueryResult([]); // object-level

        var result = await new PermissionReader().ReadAsync(runner, CancellationToken.None);

        var perm = Assert.Single(result);
        Assert.Equal("app_user", perm.GranteePrincipal);
        Assert.Null(perm.OnObject);
        Assert.Null(perm.OnSchema);
    }

    [Fact]
    public async Task ReadAsync_MapsSchemaLevelPermission()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult([]); // database-level
        runner.EnqueueQueryResult(
        [
            new Dictionary<string, object?> { ["GranteePrincipal"] = "app_user", ["PermissionName"] = "SELECT", ["State"] = "GRANT", ["SchemaName"] = "dbo" }
        ]);
        runner.EnqueueQueryResult([]); // object-level

        var result = await new PermissionReader().ReadAsync(runner, CancellationToken.None);

        var perm = Assert.Single(result);
        Assert.Equal("dbo", perm.OnSchema);
        Assert.Null(perm.OnObject);
    }

    [Fact]
    public async Task ReadAsync_MapsObjectAndColumnLevelPermission()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult([]); // database-level
        runner.EnqueueQueryResult([]); // schema-level
        runner.EnqueueQueryResult(
        [
            new Dictionary<string, object?>
            {
                ["GranteePrincipal"] = "app_user", ["PermissionName"] = "SELECT", ["State"] = "GRANT",
                ["SchemaName"] = "dbo", ["ObjectName"] = "Orders", ["ObjectTypeCode"] = "U", ["ColumnName"] = null
            },
            new Dictionary<string, object?>
            {
                ["GranteePrincipal"] = "app_user", ["PermissionName"] = "UPDATE", ["State"] = "DENY",
                ["SchemaName"] = "dbo", ["ObjectName"] = "Orders", ["ObjectTypeCode"] = "U", ["ColumnName"] = "Salary"
            }
        ]);

        var result = await new PermissionReader().ReadAsync(runner, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, p => p.OnObject!.Name == "Orders" && p.OnColumn is null && p.PermissionName == "SELECT");
        Assert.Contains(result, p => p.OnColumn == "Salary" && p.State == "DENY");
    }

    [Fact]
    public async Task ReadAsync_SkipsUnmappedObjectTypes()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueQueryResult([]); // database-level
        runner.EnqueueQueryResult([]); // schema-level
        runner.EnqueueQueryResult(
        [
            new Dictionary<string, object?>
            {
                ["GranteePrincipal"] = "app_user", ["PermissionName"] = "SELECT", ["State"] = "GRANT",
                ["SchemaName"] = "dbo", ["ObjectName"] = "SomeQueue", ["ObjectTypeCode"] = "SQ", ["ColumnName"] = null
            }
        ]);

        var result = await new PermissionReader().ReadAsync(runner, CancellationToken.None);

        Assert.Empty(result);
    }
}
