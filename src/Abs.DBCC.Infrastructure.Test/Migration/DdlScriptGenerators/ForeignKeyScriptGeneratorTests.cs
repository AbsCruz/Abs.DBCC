using Abs.DBCC.Domain.Snapshot;
using Abs.DBCC.Infrastructure.Migration.DdlScriptGenerators;

namespace Abs.DBCC.Infrastructure.Test.Migration.DdlScriptGenerators;

public class ForeignKeyScriptGeneratorTests
{
    private static ForeignKeySnapshot Fk(string deleteAction, string updateAction, bool notForReplication = false) =>
        new(
            "FK_Orders_Customers",
            new ObjectRef("dbo", "Orders", DatabaseObjectKind.Table),
            new ObjectRef("dbo", "Customers", DatabaseObjectKind.Table),
            [new ForeignKeyColumnSnapshot("CustomerId", "Id")],
            deleteAction, updateAction, notForReplication);

    [Fact]
    public void GenerateDrop_DropsConstraintOnParentTable()
    {
        var sql = ForeignKeyScriptGenerator.GenerateDrop(Fk("NO_ACTION", "NO_ACTION"));

        Assert.Equal("ALTER TABLE [dbo].[Orders] DROP CONSTRAINT [FK_Orders_Customers];", sql);
    }

    [Fact]
    public void GenerateCreate_FormatsActionsWithSpacesInsteadOfUnderscores()
    {
        var sql = ForeignKeyScriptGenerator.GenerateCreate(Fk("SET_NULL", "CASCADE"));

        Assert.Equal(
            "ALTER TABLE [dbo].[Orders] ADD CONSTRAINT [FK_Orders_Customers] FOREIGN KEY ([CustomerId]) " +
            "REFERENCES [dbo].[Customers] ([Id]) ON DELETE SET NULL ON UPDATE CASCADE;",
            sql);
    }

    [Fact]
    public void GenerateCreate_NotForReplication_AppendsClause()
    {
        var sql = ForeignKeyScriptGenerator.GenerateCreate(Fk("NO_ACTION", "NO_ACTION", notForReplication: true));

        Assert.EndsWith("NOT FOR REPLICATION;", sql);
    }
}
