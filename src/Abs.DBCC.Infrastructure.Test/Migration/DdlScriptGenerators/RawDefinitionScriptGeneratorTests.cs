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
}
