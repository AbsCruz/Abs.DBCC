using Abs.DBCC.Domain.Collation;

namespace Abs.DBCC.Domain.Test;

public class SqlCollationNameTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_ForBlankValue(string? value)
    {
        Assert.Throws<ArgumentException>(() => new SqlCollationName(value!));
    }

    [Fact]
    public void Constructor_AcceptsValidValue()
    {
        var collation = new SqlCollationName("Latin1_General_CI_AS");

        Assert.Equal("Latin1_General_CI_AS", collation.Value);
        Assert.Equal("Latin1_General_CI_AS", collation.ToString());
    }

    [Fact]
    public void Equality_IsValueBased()
    {
        var a = new SqlCollationName("Latin1_General_CI_AS");
        var b = new SqlCollationName("Latin1_General_CI_AS");

        Assert.Equal(a, b);
    }
}
