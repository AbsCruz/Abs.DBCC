using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Migration.DdlScriptGenerators;

namespace Abs.DBCC.Infrastructure.Test.Migration.DdlScriptGenerators;

public class CheckConstraintScriptGeneratorTests
{
    private static readonly ObjectRef Table = new("dbo", "Orders", DatabaseObjectKind.Table);

    [Fact]
    public void GenerateDrop_DropsConstraint()
    {
        var constraint = new CheckConstraintSnapshot("CK_Orders_Amount", "Amount", "([Amount]>(0))");

        var sql = CheckConstraintScriptGenerator.GenerateDrop(Table, constraint);

        Assert.Equal("ALTER TABLE [dbo].[Orders] DROP CONSTRAINT [CK_Orders_Amount];", sql);
    }

    [Fact]
    public void GenerateCreate_ReusesCapturedDefinitionVerbatim()
    {
        var constraint = new CheckConstraintSnapshot("CK_Orders_Amount", "Amount", "([Amount]>(0))");

        var sql = CheckConstraintScriptGenerator.GenerateCreate(Table, constraint);

        Assert.Equal("ALTER TABLE [dbo].[Orders] ADD CONSTRAINT [CK_Orders_Amount] CHECK ([Amount]>(0));", sql);
    }
}

public class DefaultConstraintScriptGeneratorTests
{
    private static readonly ObjectRef Table = new("dbo", "Orders", DatabaseObjectKind.Table);

    [Fact]
    public void GenerateDrop_DropsConstraint()
    {
        var constraint = new DefaultConstraintSnapshot("DF_Orders_Status", "Status", "('Pending')");

        var sql = DefaultConstraintScriptGenerator.GenerateDrop(Table, constraint);

        Assert.Equal("ALTER TABLE [dbo].[Orders] DROP CONSTRAINT [DF_Orders_Status];", sql);
    }

    [Fact]
    public void GenerateCreate_IncludesForColumnClause()
    {
        var constraint = new DefaultConstraintSnapshot("DF_Orders_Status", "Status", "('Pending')");

        var sql = DefaultConstraintScriptGenerator.GenerateCreate(Table, constraint);

        Assert.Equal("ALTER TABLE [dbo].[Orders] ADD CONSTRAINT [DF_Orders_Status] DEFAULT ('Pending') FOR [Status];", sql);
    }
}

public class ComputedColumnScriptGeneratorTests
{
    private static readonly ObjectRef Table = new("dbo", "Orders", DatabaseObjectKind.Table);

    private static ColumnSnapshot ComputedColumn(bool persisted) =>
        new("FullName", "nvarchar", null, null, null, true, null, true, "[FirstName]+' '+[LastName]", persisted);

    [Fact]
    public void GenerateDrop_DropsColumn()
    {
        var sql = ComputedColumnScriptGenerator.GenerateDrop(Table, ComputedColumn(false));

        Assert.Equal("ALTER TABLE [dbo].[Orders] DROP COLUMN [FullName];", sql);
    }

    [Fact]
    public void GenerateCreate_Persisted_AppendsPersistedKeyword()
    {
        var sql = ComputedColumnScriptGenerator.GenerateCreate(Table, ComputedColumn(true));

        Assert.Equal("ALTER TABLE [dbo].[Orders] ADD [FullName] AS [FirstName]+' '+[LastName] PERSISTED;", sql);
    }

    [Fact]
    public void GenerateCreate_NotPersisted_OmitsPersistedKeyword()
    {
        var sql = ComputedColumnScriptGenerator.GenerateCreate(Table, ComputedColumn(false));

        Assert.Equal("ALTER TABLE [dbo].[Orders] ADD [FullName] AS [FirstName]+' '+[LastName];", sql);
    }
}
