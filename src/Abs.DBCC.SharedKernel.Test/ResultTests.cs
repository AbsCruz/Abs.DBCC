using Abs.DBCC.SharedKernel;

namespace Abs.DBCC.SharedKernel.Test;

public class ResultTests
{
    [Fact]
    public void Success_HasNoError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_CarriesErrorMessage()
    {
        var result = Result.Failure("something went wrong");

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal("something went wrong", result.Error);
    }

    [Fact]
    public void GenericSuccess_ExposesValue()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void GenericFailure_ThrowsWhenAccessingValue()
    {
        var result = Result.Failure<int>("nope");

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }
}

public class GuardTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AgainstNullOrWhiteSpace_Throws_ForBlankInput(string? value)
    {
        Assert.Throws<ArgumentException>(() => Guard.AgainstNullOrWhiteSpace(value, "param"));
    }

    [Fact]
    public void AgainstNullOrWhiteSpace_ReturnsValue_ForNonBlankInput()
    {
        var result = Guard.AgainstNullOrWhiteSpace("hello", "param");

        Assert.Equal("hello", result);
    }

    [Fact]
    public void AgainstNull_Throws_ForNull()
    {
        Assert.Throws<ArgumentNullException>(() => Guard.AgainstNull<string>(null, "param"));
    }
}
