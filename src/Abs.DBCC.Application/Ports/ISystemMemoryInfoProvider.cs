namespace Abs.DBCC.Application.Ports;

public interface ISystemMemoryInfoProvider
{
    /// <summary>Currently free and total installed physical memory, in bytes.</summary>
    (long AvailableBytes, long TotalBytes) GetPhysicalMemory();
}
