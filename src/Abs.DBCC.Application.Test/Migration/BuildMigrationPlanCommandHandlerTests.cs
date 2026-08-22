using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Migration;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Migration;
using Abs.DBCC.TestCommon.Builders;
using Moq;

namespace Abs.DBCC.Application.Test.Migration;

public class BuildMigrationPlanCommandHandlerTests
{
    [Fact]
    public async Task Handle_CapturesSnapshotThenBuildsPlan()
    {
        var profile = new ConnectionProfile("server", "db", "user", "pw");
        var target = new SqlCollationName("Latin1_General_100_CI_AS_SC_UTF8");
        var snapshot = new DatabaseSnapshotBuilder().Build();
        var expectedPlan = new MigrationPlan(snapshot.DatabaseCollation, target, true, snapshot, [], []);

        var snapshotService = new Mock<ISchemaSnapshotService>();
        snapshotService.Setup(s => s.CaptureAsync(profile, It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);

        var planBuilder = new Mock<IMigrationPlanBuilder>();
        planBuilder.Setup(b => b.Build(snapshot, target, true)).Returns(expectedPlan);

        var handler = new BuildMigrationPlanCommandHandler(snapshotService.Object, planBuilder.Object);

        var result = await handler.Handle(new BuildMigrationPlanCommand(profile, target, true), CancellationToken.None);

        Assert.Same(expectedPlan, result);
        snapshotService.Verify(s => s.CaptureAsync(profile, It.IsAny<CancellationToken>()), Times.Once);
        planBuilder.Verify(b => b.Build(snapshot, target, true), Times.Once);
    }
}
