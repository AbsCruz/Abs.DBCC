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
        // sys.sql_modules.definition often ends in a trailing newline; trimming it before replaying
        // would make the recreated definition differ from the original and register as a false
        // structural diff.
        var obj = new ObjectDefinition(new ObjectRef("dbo", "V1", DatabaseObjectKind.View), "CREATE VIEW dbo.V1 AS SELECT 1;\n", false);

        var sql = RawDefinitionScriptGenerator.GenerateCreate(obj);

        Assert.Equal("CREATE VIEW dbo.V1 AS SELECT 1;\n", sql);
    }

    [Fact]
    public void GenerateCreate_DefinitionEmbedsOldNameFromABygoneSpRename_RewritesHeaderToTheCurrentName()
    {
        // sp_rename updates sys.objects.name but not sys.sql_modules.definition, so a renamed view's
        // stored CREATE VIEW text still has its original name. Replaying it verbatim after a DROP
        // recreates the view under the old name, breaking any later step that references the current name.
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
        // sys.sql_modules.definition stores the last-run statement verbatim; replaying a captured ALTER
        // against an object that was just dropped fails outright, since ALTER requires the object to
        // already exist.
        var obj = new ObjectDefinition(new ObjectRef("dbo", "V1", DatabaseObjectKind.View), "ALTER VIEW dbo.V1 AS SELECT 2;", false);

        var sql = RawDefinitionScriptGenerator.GenerateCreate(obj);

        Assert.Equal("CREATE VIEW [dbo].[V1] AS SELECT 2;", sql);
    }

    [Fact]
    public void GenerateCreate_NameAlreadyMatchesCurrentIdentity_ReplaysCompletelyUntouched()
    {
        // The common case must stay byte-for-byte identical, including quoting/spacing - rewriting it
        // unconditionally would make every recreated definition differ from the original and show up as
        // a false structural diff.
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
        // Unbracketed, multi-line form, distinct from the bracketed-name case above - verifies the
        // header regex matches this shape too.
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
        // SQL Server allows leading "--" comments (and SET ANSI_NULLS/QUOTED_IDENTIFIER) before CREATE
        // VIEW in the same batch, and sys.sql_modules.definition captures them too - the header search
        // must skip past them without swallowing them into the rewritten header.
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
