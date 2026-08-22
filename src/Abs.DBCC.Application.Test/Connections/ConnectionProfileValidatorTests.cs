using Abs.DBCC.Application.Connections;

namespace Abs.DBCC.Application.Test.Connections;

public class ConnectionProfileValidatorTests
{
    private readonly ConnectionProfileValidator _validator = new();

    [Fact]
    public void Validate_Succeeds_ForCompleteProfile()
    {
        var result = _validator.Validate(new ConnectionProfile("server", "db", "user", "pw"));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("", "db", "user", "pw")]
    [InlineData("server", "", "user", "pw")]
    [InlineData("server", "db", "", "pw")]
    [InlineData("server", "db", "user", "")]
    public void Validate_Fails_WhenRequiredFieldIsEmpty(string server, string database, string user, string password)
    {
        var result = _validator.Validate(new ConnectionProfile(server, database, user, password));

        Assert.False(result.IsValid);
    }
}
