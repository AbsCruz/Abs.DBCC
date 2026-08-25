using System.Runtime.InteropServices;
using Abs.DBCC.Application.Ports;

namespace Abs.DBCC.Infrastructure.Environment;

/// <summary>Reads current physical memory status via the Windows API - this tool only ships as a Windows desktop app.</summary>
public sealed class SystemMemoryInfoProvider : ISystemMemoryInfoProvider
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public (long AvailableBytes, long TotalBytes) GetPhysicalMemory()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref status)
            ? ((long)status.ullAvailPhys, (long)status.ullTotalPhys)
            : (0L, 0L);
    }
}
