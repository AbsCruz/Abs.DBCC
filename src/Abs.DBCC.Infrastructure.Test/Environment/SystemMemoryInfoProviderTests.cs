using Abs.DBCC.Infrastructure.Environment;

namespace Abs.DBCC.Infrastructure.Test.Environment;

public class SystemMemoryInfoProviderTests
{
    [Fact]
    public void GetPhysicalMemory_ReturnsAPositiveReading_RegardlessOfOperatingSystem()
    {
        // Reproduces a real CI failure: GlobalMemoryStatusEx is a Windows-only kernel32.dll P/Invoke,
        // but this type is constructed through DI by code that also runs on Linux (the Testcontainers
        // integration tests, via a real PreflightCheckService) - calling it unconditionally threw
        // DllNotFoundException on every Linux CI run, deterministically, not just as a flake. Every
        // platform the app ships for (Windows, macOS x64/arm64, Linux - see the publish matrix in
        // .github/workflows/build-test-release.yml) now has its own real reading rather than a
        // "didn't crash" (0, 0) placeholder, so any real machine has some memory. This test only
        // exercises whatever OS it happens to run on - the CI "test" job is Ubuntu-only, so the macOS
        // branch (sysctl/vm_stat) has no automated coverage yet; it would need a macos-latest runner in
        // the test job's matrix to actually verify it, since it's unverifiable on Windows/Linux.
        var sut = new SystemMemoryInfoProvider();

        var (available, total) = sut.GetPhysicalMemory();

        Assert.True(available > 0);
        Assert.True(total > 0);
        Assert.True(available <= total);
    }
}
