using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Migration;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.TestCommon.Builders;
using Moq;

namespace Abs.DBCC.Application.Test.Migration;

public class GetPreflightCheckQueryHandlerTests
{
    [Fact]
    public async Task Handle_DelegatesToPreflightCheckService()
    {
        var profile = new ConnectionProfile("server", "db", "user", "pw");
        var target = new SqlCollationName("Latin1_General_100_CI_AS_SC_UTF8");
        var plan = new MigrationPlan(target, target, false, new DatabaseSnapshotBuilder().Build(), [], []);
        var expected = new PreflightCheckResult(1, 42, 1_000_000, 10.0);

        var service = new Mock<IPreflightCheckService>();
        service.Setup(s => s.CheckAsync(profile, plan, It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var handler = new GetPreflightCheckQueryHandler(service.Object);

        var result = await handler.Handle(new GetPreflightCheckQuery(profile, plan), CancellationToken.None);

        Assert.Same(expected, result);
    }
}
