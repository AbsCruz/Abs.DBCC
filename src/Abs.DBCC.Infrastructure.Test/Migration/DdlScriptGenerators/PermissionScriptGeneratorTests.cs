using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Migration.DdlScriptGenerators;

namespace Abs.DBCC.Infrastructure.Test.Migration.DdlScriptGenerators;

public class PermissionScriptGeneratorTests
{
    private static readonly ObjectRef Table = new("dbo", "Orders", DatabaseObjectKind.Table);

    [Fact]
    public void Generate_DatabaseLevelGrant_HasNoOnClause()
    {
        var permission = new PermissionSnapshot("app_user", "CONNECT", "GRANT", null, null);

        var sql = PermissionScriptGenerator.Generate(permission);

        Assert.Equal("GRANT CONNECT TO [app_user];", sql);
    }

    [Fact]
    public void Generate_ObjectLevelGrant_IncludesOnClause()
    {
        var permission = new PermissionSnapshot("app_user", "SELECT", "GRANT", Table, null);

        var sql = PermissionScriptGenerator.Generate(permission);

        Assert.Equal("GRANT SELECT ON [dbo].[Orders] TO [app_user];", sql);
    }

    [Fact]
    public void Generate_ColumnLevelGrant_IncludesColumnInParens()
    {
        var permission = new PermissionSnapshot("app_user", "UPDATE", "GRANT", Table, "Salary");

        var sql = PermissionScriptGenerator.Generate(permission);

        Assert.Equal("GRANT UPDATE ON [dbo].[Orders]([Salary]) TO [app_user];", sql);
    }

    [Fact]
    public void Generate_Deny_UsesDenyVerb()
    {
        var permission = new PermissionSnapshot("app_user", "DELETE", "DENY", Table, null);

        var sql = PermissionScriptGenerator.Generate(permission);

        Assert.StartsWith("DENY DELETE", sql);
    }

    [Fact]
    public void Generate_GrantWithGrantOption_AppendsWithGrantOption()
    {
        var permission = new PermissionSnapshot("app_user", "SELECT", "GRANT_WITH_GRANT_OPTION", Table, null);

        var sql = PermissionScriptGenerator.Generate(permission);

        Assert.EndsWith("WITH GRANT OPTION;", sql);
    }

    [Fact]
    public void Generate_SchemaLevelGrant_UsesSchemaSyntax()
    {
        var permission = new PermissionSnapshot("app_user", "SELECT", "GRANT", null, null, "dbo");

        var sql = PermissionScriptGenerator.Generate(permission);

        Assert.Equal("GRANT SELECT ON SCHEMA::[dbo] TO [app_user];", sql);
    }
}
