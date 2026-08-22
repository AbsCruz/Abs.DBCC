using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.TestCommon.Fakes;
using Moq;

namespace Abs.DBCC.Application.Test.Connections;

public class TestConnectionQueryHandlerTests
{
    private static readonly ConnectionProfile Profile = new("server", "db", "user", "pw");

    [Fact]
    public async Task Handle_ReturnsSuccess_WhenQueryExecutesWithoutError()
    {
        var runner = new FakeSqlScriptRunner();
        runner.EnqueueScalarResult(1);
        var factory = new Mock<ISqlScriptRunnerFactory>();
        factory.Setup(f => f.CreateAsync(Profile, It.IsAny<CancellationToken>())).ReturnsAsync(runner);

        var handler = new TestConnectionQueryHandler(factory.Object);

        var result = await handler.Handle(new TestConnectionQuery(Profile), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenConnectionThrows()
    {
        var factory = new Mock<ISqlScriptRunnerFactory>();
        factory
            .Setup(f => f.CreateAsync(Profile, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("connection refused"));

        var handler = new TestConnectionQueryHandler(factory.Object);

        var result = await handler.Handle(new TestConnectionQuery(Profile), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("connection refused", result.Error);
    }
}
