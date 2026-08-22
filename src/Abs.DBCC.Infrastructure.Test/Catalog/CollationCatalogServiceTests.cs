using Abs.DBCC.Application.Connections;
using Abs.DBCC.Application.Ports;
using Abs.DBCC.Infrastructure.Catalog;
using Abs.DBCC.TestCommon.Fakes;
using Moq;

namespace Abs.DBCC.Infrastructure.Test.Catalog;

public class CollationCatalogServiceTests
{
    private static readonly ConnectionProfile Profile = new("server", "db", "user", "pw");

    private static (CollationCatalogService Service, FakeSqlScriptRunner Runner) CreateSut()
    {
        var runner = new FakeSqlScriptRunner();
        var factory = new Mock<ISqlScriptRunnerFactory>();
        factory.Setup(f => f.CreateAsync(Profile, It.IsAny<CancellationToken>())).ReturnsAsync(runner);

        return (new CollationCatalogService(factory.Object), runner);
    }

    [Fact]
    public async Task GetServerDefaultCollationAsync_ReturnsScalarResult()
    {
        var (service, runner) = CreateSut();
        runner.EnqueueScalarResult("SQL_Latin1_General_CP1_CI_AS");

        var result = await service.GetServerDefaultCollationAsync(Profile);

        Assert.Equal("SQL_Latin1_General_CP1_CI_AS", result.Value);
    }

    [Fact]
    public async Task GetDatabaseDefaultCollationAsync_ReturnsScalarResult()
    {
        var (service, runner) = CreateSut();
        runner.EnqueueScalarResult("Latin1_General_100_CI_AS_SC_UTF8");

        var result = await service.GetDatabaseDefaultCollationAsync(Profile);

        Assert.Equal("Latin1_General_100_CI_AS_SC_UTF8", result.Value);
    }

    [Fact]
    public async Task GetAvailableCollationsAsync_MapsAllRows()
    {
        var (service, runner) = CreateSut();
        runner.EnqueueQueryResult(
        [
            new Dictionary<string, object?> { ["name"] = "Latin1_General_CI_AS", ["description"] = "Latin1-General, case-insensitive, accent-sensitive" },
            new Dictionary<string, object?> { ["name"] = "Latin1_General_CS_AS", ["description"] = "Latin1-General, case-sensitive, accent-sensitive" }
        ]);

        var result = await service.GetAvailableCollationsAsync(Profile);

        Assert.Equal(2, result.Count);
        Assert.Equal("Latin1_General_CI_AS", result[0].Name);
        Assert.Equal("Latin1-General, case-sensitive, accent-sensitive", result[1].Description);
    }

    [Fact]
    public async Task GetServerDefaultCollationAsync_DisposesRunner()
    {
        var (service, runner) = CreateSut();
        runner.EnqueueScalarResult("SQL_Latin1_General_CP1_CI_AS");

        await service.GetServerDefaultCollationAsync(Profile);

        Assert.True(runner.IsDisposed);
    }
}
