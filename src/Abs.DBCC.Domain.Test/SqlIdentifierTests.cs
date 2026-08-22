using Abs.DBCC.Domain.Common;

namespace Abs.DBCC.Domain.Test;

public class SqlIdentifierTests
{
    [Fact]
    public void Quoted_BracketsSchemaAndName()
    {
        var identifier = new SqlIdentifier("dbo", "Orders");

        Assert.Equal("[dbo].[Orders]", identifier.Quoted);
    }

    [Fact]
    public void QuotePart_EscapesEmbeddedClosingBracket()
    {
        var quoted = SqlIdentifier.QuotePart("Weird]Name");

        Assert.Equal("[Weird]]Name]", quoted);
    }

    [Theory]
    [InlineData(null, "name")]
    [InlineData("", "name")]
    [InlineData("schema", null)]
    [InlineData("schema", "")]
    public void Constructor_Throws_ForBlankParts(string? schema, string? name)
    {
        Assert.Throws<ArgumentException>(() => new SqlIdentifier(schema!, name!));
    }
}
