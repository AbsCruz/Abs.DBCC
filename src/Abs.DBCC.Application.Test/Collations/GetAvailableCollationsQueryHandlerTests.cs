using Abs.DBCC.Application.Collations;
using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Domain.Collation;
using Moq;

namespace Abs.DBCC.Application.Test.Collations;

public class GetAvailableCollationsQueryHandlerTests
{
    [Fact]
    public async Task Handle_DelegatesToCatalogService()
    {
        var profile = new ConnectionProfile("server", "db", "user", "pw");
        var expected = new List<CollationInfo> { new("Latin1_General_CI_AS", "description") };

        var catalogService = new Mock<ICollationCatalogService>();
        catalogService
            .Setup(c => c.GetAvailableCollationsAsync(profile, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var handler = new GetAvailableCollationsQueryHandler(catalogService.Object);

        var result = await handler.Handle(new GetAvailableCollationsQuery(profile), CancellationToken.None);

        Assert.Same(expected, result);
    }
}
