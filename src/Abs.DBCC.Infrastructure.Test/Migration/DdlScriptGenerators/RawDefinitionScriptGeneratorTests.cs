using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Migration.DdlScriptGenerators;

namespace Abs.DBCC.Infrastructure.Test.Migration.DdlScriptGenerators;

public class RawDefinitionScriptGeneratorTests
{
    [Theory]
    [InlineData(DatabaseObjectKind.View, "VIEW")]
    [InlineData(DatabaseObjectKind.StoredProcedure, "PROCEDURE")]
    [InlineData(DatabaseObjectKind.Function, "FUNCTION")]
    [InlineData(DatabaseObjectKind.Trigger, "TRIGGER")]
    public void GenerateDrop_UsesCorrectKeywordPerKind(DatabaseObjectKind kind, string expectedKeyword)
    {
        var obj = new ObjectDefinition(new ObjectRef("dbo", "Thing", kind), "CREATE ...", false);

        var sql = RawDefinitionScriptGenerator.GenerateDrop(obj);

        Assert.Equal($"DROP {expectedKeyword} [dbo].[Thing];", sql);
    }

    [Fact]
    public void GenerateCreate_ReplaysDefinitionVerbatim_AddingTrailingSemicolonIfMissing()
    {
        var obj = new ObjectDefinition(new ObjectRef("dbo", "V1", DatabaseObjectKind.View), "CREATE VIEW dbo.V1 AS SELECT 1", false);

        var sql = RawDefinitionScriptGenerator.GenerateCreate(obj);

        Assert.Equal("CREATE VIEW dbo.V1 AS SELECT 1;", sql);
    }

    [Fact]
    public void GenerateCreate_DoesNotDuplicateExistingTrailingSemicolon()
    {
        var obj = new ObjectDefinition(new ObjectRef("dbo", "V1", DatabaseObjectKind.View), "CREATE VIEW dbo.V1 AS SELECT 1;", false);

        var sql = RawDefinitionScriptGenerator.GenerateCreate(obj);

        Assert.Equal("CREATE VIEW dbo.V1 AS SELECT 1;", sql);
    }

    [Fact]
    public void GenerateCreate_PreservesTrailingWhitespaceOfTheCapturedDefinition()
    {
        // sys.sql_modules.definition frequently ends in a trailing newline (whatever was originally
        // submitted); trimming it before replaying would make the recreated object's own captured
        // definition differ from the original one - a real structural-diff false positive found by
        // the Testcontainers integration test against a real SQL Server.
        var obj = new ObjectDefinition(new ObjectRef("dbo", "V1", DatabaseObjectKind.View), "CREATE VIEW dbo.V1 AS SELECT 1;\n", false);

        var sql = RawDefinitionScriptGenerator.GenerateCreate(obj);

        Assert.Equal("CREATE VIEW dbo.V1 AS SELECT 1;\n", sql);
    }

    [Fact]
    public void GenerateCreate_DefinitionEmbedsOldNameFromABygoneSpRename_RewritesHeaderToTheCurrentName()
    {
        // Reproduces a real-world failure: sp_rename updates sys.objects.name but never touches
        // sys.sql_modules.definition, so a view renamed at some point still has its ORIGINAL name
        // baked into its stored CREATE VIEW text. Replaying that text verbatim after a DROP silently
        // recreates the view under the *old* name - the very next step (e.g. its own index, scoped to
        // the current name) then fails with "object not found".
        var obj = new ObjectDefinition(
            new ObjectRef("dbo", "_vOrderSummary", DatabaseObjectKind.View),
            "CREATE VIEW [dbo].[vOrderSummary] WITH SCHEMABINDING AS SELECT Id FROM dbo.T;",
            true);

        var sql = RawDefinitionScriptGenerator.GenerateCreate(obj);

        Assert.Equal("CREATE VIEW [dbo].[_vOrderSummary] WITH SCHEMABINDING AS SELECT Id FROM dbo.T;", sql);
    }

    [Fact]
    public void GenerateCreate_DefinitionUsesAlter_RewritesToCreate()
    {
        // sys.sql_modules.definition stores whatever statement was last run verbatim - if that was an
        // ALTER (a normal, non-renaming edit), replaying it against an object that was just DROPped
        // fails outright, since ALTER requires the object to already exist.
        var obj = new ObjectDefinition(new ObjectRef("dbo", "V1", DatabaseObjectKind.View), "ALTER VIEW dbo.V1 AS SELECT 2;", false);

        var sql = RawDefinitionScriptGenerator.GenerateCreate(obj);

        Assert.Equal("CREATE VIEW [dbo].[V1] AS SELECT 2;", sql);
    }

    [Fact]
    public void GenerateCreate_NameAlreadyMatchesCurrentIdentity_ReplaysCompletelyUntouched()
    {
        // The common case (no rename, no stale ALTER) must stay byte-for-byte identical, including its
        // original quoting/spacing style - rewriting it unconditionally would make every recreated
        // object's captured definition differ from the original and show up as a false structural diff.
        var obj = new ObjectDefinition(new ObjectRef("dbo", "V1", DatabaseObjectKind.View), "CREATE   VIEW  dbo.V1  AS SELECT 1;", false);

        var sql = RawDefinitionScriptGenerator.GenerateCreate(obj);

        Assert.Equal("CREATE   VIEW  dbo.V1  AS SELECT 1;", sql);
    }

    [Fact]
    public void GenerateCreate_SchemaDiffersFromCurrentIdentity_RewritesHeader()
    {
        var obj = new ObjectDefinition(
            new ObjectRef("reporting", "V1", DatabaseObjectKind.View),
            "CREATE VIEW dbo.V1 AS SELECT 1;",
            false);

        var sql = RawDefinitionScriptGenerator.GenerateCreate(obj);

        Assert.Equal("CREATE VIEW [reporting].[V1] AS SELECT 1;", sql);
    }

    [Fact]
    public void GenerateCreate_RenamedMultilineSchemaBoundViewUnbracketedName_RewritesHeader()
    {
        // Mirrors the exact multi-line, unbracketed text form used by the integration test schema
        // (and plausibly by many hand-written CREATE VIEW statements) - isolates whether the header
        // regex itself matches this shape, independent of the bracketed-name unit test above.
        var obj = new ObjectDefinition(
            new ObjectRef("dbo", "OrdersByCustomerCodeRenamed", DatabaseObjectKind.View),
            "CREATE VIEW dbo.OrdersByCustomerCodeOriginal WITH SCHEMABINDING AS\n    SELECT Id, CustomerCode FROM dbo.Orders;",
            true);

        var sql = RawDefinitionScriptGenerator.GenerateCreate(obj);

        Assert.Equal("CREATE VIEW [dbo].[OrdersByCustomerCodeRenamed] WITH SCHEMABINDING AS\n    SELECT Id, CustomerCode FROM dbo.Orders;", sql);
    }

    [Fact]
    public void GenerateCreate_RenamedViewWithLeadingLineComments_SkipsCommentsAndPreservesThem()
    {
        // SQL Server allows "--" comments (and SET ANSI_NULLS/QUOTED_IDENTIFIER) before CREATE VIEW in
        // the same batch, and captures them as part of sys.sql_modules.definition too - the header
        // search must skip past them to find the actual CREATE clause, and must preserve them verbatim
        // rather than swallowing them into the rewritten header.
        var obj = new ObjectDefinition(
            new ObjectRef("dbo", "OrdersByCustomerCodeRenamed", DatabaseObjectKind.View),
            "-- Author: someone\n-- Purpose: summary view\nCREATE VIEW dbo.OrdersByCustomerCodeOriginal WITH SCHEMABINDING AS\n    SELECT Id FROM dbo.Orders;",
            true);

        var sql = RawDefinitionScriptGenerator.GenerateCreate(obj);

        Assert.Equal(
            "-- Author: someone\n-- Purpose: summary view\nCREATE VIEW [dbo].[OrdersByCustomerCodeRenamed] WITH SCHEMABINDING AS\n    SELECT Id FROM dbo.Orders;",
            sql);
    }

    [Fact]
    public void GenerateCreate_RenamedFunctionWithParameters_RewritesHeaderWithoutTouchingTheParameterList()
    {
        var obj = new ObjectDefinition(
            new ObjectRef("dbo", "GetOrderTotalV2", DatabaseObjectKind.Function),
            "CREATE FUNCTION dbo.GetOrderTotal (@OrderId INT) RETURNS DECIMAL(10,2) AS BEGIN RETURN 0; END;",
            false);

        var sql = RawDefinitionScriptGenerator.GenerateCreate(obj);

        Assert.Equal("CREATE FUNCTION [dbo].[GetOrderTotalV2] (@OrderId INT) RETURNS DECIMAL(10,2) AS BEGIN RETURN 0; END;", sql);
    }
}
