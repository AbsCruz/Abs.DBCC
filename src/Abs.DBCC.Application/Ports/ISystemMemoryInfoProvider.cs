namespace Abs.DBCC.Application.Ports;

/// <summary>Queries the local machine's current physical memory status.</summary>
public interface ISystemMemoryInfoProvider
{
    /// <summary>Currently free and total installed physical memory, in bytes.</summary>
    (long AvailableBytes, long TotalBytes) GetPhysicalMemory();
}
