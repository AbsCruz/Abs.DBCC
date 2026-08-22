using Abs.DBCC.Application.Collations;
using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Collation;
using Abs.DBCC.Domain.Inspection;
using Moq;

namespace Abs.DBCC.Application.Test.Collations;

public class GetDatabaseCollationReportQueryHandlerTests
{
    [Fact]
    public async Task Handle_DelegatesToInspectionService()
    {
        var profile = new ConnectionProfile("server", "db", "user", "pw");
        var expected = new DatabaseCollationReport(new SqlCollationName("Latin1_General_CI_AS"), []);

        var inspectionService = new Mock<IDatabaseInspectionService>();
        inspectionService
            .Setup(i => i.BuildCollationReportAsync(profile, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var handler = new GetDatabaseCollationReportQueryHandler(inspectionService.Object);

        var result = await handler.Handle(new GetDatabaseCollationReportQuery(profile), CancellationToken.None);

        Assert.Same(expected, result);
    }
}
