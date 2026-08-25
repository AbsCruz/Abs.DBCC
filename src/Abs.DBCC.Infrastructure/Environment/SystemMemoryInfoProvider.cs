using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Abs.DBCC.Application.Ports;

namespace Abs.DBCC.Infrastructure.Environment;

/// <summary>
/// Reads current physical memory status using whatever the platform actually offers - the app itself
/// ships for Windows, macOS (x64/arm64) and Linux (see the publish matrix in
/// .github/workflows/build-test-release.yml), and this type is additionally constructed (through DI) by
/// code that runs in CI on Linux (the Testcontainers-based integration tests), so every one of those
/// needs a real reading rather than a placeholder.
/// </summary>
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
        if (OperatingSystem.IsWindows())
            return GetWindowsPhysicalMemory();

        if (OperatingSystem.IsLinux())
            return GetLinuxPhysicalMemory();

        if (OperatingSystem.IsMacOS())
            return GetMacOsPhysicalMemory();

        return (0L, 0L);
    }

    private static (long AvailableBytes, long TotalBytes) GetWindowsPhysicalMemory()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref status)
            ? ((long)status.ullAvailPhys, (long)status.ullTotalPhys)
            : (0L, 0L);
    }

    /// <summary>
    /// /proc/meminfo is part of the kernel's procfs, always present on Linux, and requires no elevated
    /// permissions to read - "MemTotal"/"MemAvailable" are both reported in kB.
    /// </summary>
    private static (long AvailableBytes, long TotalBytes) GetLinuxPhysicalMemory()
    {
        try
        {
            long? totalKb = null;
            long? availableKb = null;

            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                    totalKb = ParseMeminfoValueKb(line);
                else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                    availableKb = ParseMeminfoValueKb(line);

                if (totalKb is not null && availableKb is not null)
                    break;
            }

            return (availableKb is null ? 0L : availableKb.Value * 1024, totalKb is null ? 0L : totalKb.Value * 1024);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            return (0L, 0L);
        }
    }

    private static long ParseMeminfoValueKb(string line)
    {
        // e.g. "MemTotal:       16384000 kB" - the value is always the second whitespace-separated token.
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return long.Parse(parts[1], CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// macOS has no single procfs-style file for this (and no BCL API either) - total physical memory
    /// comes from the "hw.memsize" sysctl, and "available" is approximated as free + inactive +
    /// speculative pages from vm_stat (all three are pages the kernel can hand out immediately or
    /// reclaim without paging anything out - the same approximation Activity Monitor's "Memory Used"
    /// figure is built from), converted to bytes via the page size vm_stat itself reports.
    /// </summary>
    private static (long AvailableBytes, long TotalBytes) GetMacOsPhysicalMemory()
    {
        try
        {
            var totalBytes = ParseLong(RunProcess("/usr/sbin/sysctl", "-n hw.memsize"));
            if (totalBytes is not > 0)
                return (0L, 0L);

            var vmStatOutput = RunProcess("/usr/bin/vm_stat", string.Empty);
            if (vmStatOutput is null)
                return (0L, totalBytes.Value);

            var pageSizeMatch = Regex.Match(vmStatOutput, @"page size of (\d+) bytes");
            var pageSize = pageSizeMatch.Success ? long.Parse(pageSizeMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 4096L;

            var availablePages =
                ParseVmStatPages(vmStatOutput, "Pages free") +
                ParseVmStatPages(vmStatOutput, "Pages inactive") +
                ParseVmStatPages(vmStatOutput, "Pages speculative");

            return (availablePages * pageSize, totalBytes.Value);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception or FormatException)
        {
            return (0L, 0L);
        }
    }

    private static long ParseVmStatPages(string vmStatOutput, string label)
    {
        // e.g. "Pages free:                              123456."
        var match = Regex.Match(vmStatOutput, $@"{Regex.Escape(label)}:\s*(\d+)\.");
        return match.Success ? long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0L;
    }

    private static long? ParseLong(string? value) =>
        long.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    /// <summary>Runs a fixed, argument-free-of-user-input platform tool and returns its stdout, or null if it didn't complete within 5s.</summary>
    private static string? RunProcess(string fileName, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        if (!process.Start())
            return null;

        var output = process.StandardOutput.ReadToEnd();
        return process.WaitForExit(5000) ? output : null;
    }
}
