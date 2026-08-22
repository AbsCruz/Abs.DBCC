using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Migration.DdlScriptGenerators;

namespace Abs.DBCC.Infrastructure.Test.Migration.DdlScriptGenerators;

public class AlterColumnScriptGeneratorTests
{
    private static readonly ObjectRef Table = new("dbo", "Orders", DatabaseObjectKind.Table);
    private static readonly SqlCollationName Target = new("Latin1_General_100_CI_AS_SC_UTF8");

    private static ColumnSnapshot Column(string type, int? maxLength, bool isNullable = true) =>
        new("Name", type, maxLength, null, null, isNullable, new SqlCollationName("SQL_Latin1_General_CP1_CI_AS"), false, null, false);

    [Fact]
    public void Generate_Nvarchar_DividesByteLengthByTwo()
    {
        var sql = AlterColumnScriptGenerator.Generate(Table, Column("nvarchar", 100), Target);

        Assert.Equal("ALTER TABLE [dbo].[Orders] ALTER COLUMN [Name] nvarchar(50) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NULL;", sql);
    }

    [Fact]
    public void Generate_Varchar_UsesLengthDirectly()
    {
        var sql = AlterColumnScriptGenerator.Generate(Table, Column("varchar", 50), Target);

        Assert.Equal("ALTER TABLE [dbo].[Orders] ALTER COLUMN [Name] varchar(50) COLLATE Latin1_General_100_CI_AS_SC_UTF8 NULL;", sql);
    }

    [Fact]
    public void Generate_MaxLengthMinusOne_RendersAsMax()
    {
        var sql = AlterColumnScriptGenerator.Generate(Table, Column("nvarchar", -1), Target);

        Assert.Contains("nvarchar(MAX)", sql);
    }

    [Fact]
    public void Generate_NotNullableColumn_AppendsNotNull()
    {
        var sql = AlterColumnScriptGenerator.Generate(Table, Column("varchar", 20, isNullable: false), Target);

        Assert.EndsWith("NOT NULL;", sql);
    }

    [Fact]
    public void Generate_Text_HasNoLengthSuffix()
    {
        var sql = AlterColumnScriptGenerator.Generate(Table, Column("text", null), Target);

        Assert.Contains("ALTER COLUMN [Name] text COLLATE", sql);
    }
}
