using Abs.DBCC.Infrastructure.Environment;

namespace Abs.DBCC.Infrastructure.Test.Environment;

public class SystemMemoryInfoProviderTests
{
    [Fact]
    public void GetPhysicalMemory_ReturnsAPositiveReading_RegardlessOfOperatingSystem()
    {
        // GlobalMemoryStatusEx is a Windows-only P/Invoke, but this type is constructed via DI by code
        // that also runs on Linux (PreflightCheckService, exercised by the Testcontainers integration
        // tests) - calling it unconditionally would throw DllNotFoundException there. Each platform the
        // app ships for has its own real reading; this test only exercises whichever OS it runs on, and
        // since CI's "test" job is Ubuntu-only, the macOS branch (sysctl/vm_stat) has no automated coverage.
        var sut = new SystemMemoryInfoProvider();

        var (available, total) = sut.GetPhysicalMemory();

        Assert.True(available > 0);
        Assert.True(total > 0);
        Assert.True(available <= total);
    }
}
