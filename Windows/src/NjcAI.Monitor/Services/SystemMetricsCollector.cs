using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using NjcAI.Monitor.Core;

namespace NjcAI.Monitor.Services;

internal sealed class SystemMetricsCollector : IDisposable
{
    private readonly HardwareMonitorProvider _hardware = new();
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private long _previousNetworkRx;
    private long _previousNetworkTx;
    private DateTime _previousNetworkAt;

    public SystemSnapshot Collect()
    {
        var capturedAt = DateTime.Now;
        var nativeCpu = ReadNativeCpu();
        var hardware = _hardware.Read();
        var totalCpu = Math.Clamp(hardware.CpuTotal ?? nativeCpu, 0, 100);
        var perCore = hardware.CpuCores.Count > 0
            ? hardware.CpuCores
            : Enumerable.Repeat(totalCpu, Math.Max(1, Environment.ProcessorCount)).ToArray();

        var memory = new MemoryStatusEx();
        memory.Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        _ = GlobalMemoryStatusEx(ref memory);

        var (networkRx, networkTx) = ReadNetwork(capturedAt);

        return new SystemSnapshot(
            capturedAt,
            totalCpu,
            perCore,
            hardware.CpuTemperature,
            hardware.GpuTemperature,
            Math.Clamp(hardware.GpuLoad ?? 0, 0, 100),
            memory.TotalPhysical,
            memory.TotalPhysical >= memory.AvailablePhysical
                ? memory.TotalPhysical - memory.AvailablePhysical
                : 0,
            memory.AvailablePhysical,
            Environment.TickCount64 / 1000,
            CountProcesses(),
            networkRx,
            networkTx,
            hardware.DiskReadBytesPerSecond,
            hardware.DiskWriteBytesPerSecond);
    }

    private double ReadNativeCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return 0;
        }

        var currentIdle = idle.ToUInt64();
        var currentKernel = kernel.ToUInt64();
        var currentUser = user.ToUInt64();
        var hasPrevious = _previousKernel != 0;
        var idleDelta = currentIdle - _previousIdle;
        var totalDelta = currentKernel - _previousKernel + currentUser - _previousUser;
        _previousIdle = currentIdle;
        _previousKernel = currentKernel;
        _previousUser = currentUser;
        if (!hasPrevious || totalDelta == 0)
        {
            return 0;
        }

        return Math.Clamp((totalDelta - idleDelta) * 100d / totalDelta, 0, 100);
    }

    private (double Receive, double Send) ReadNetwork(DateTime now)
    {
        long receive = 0;
        long send = 0;
        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }
                var statistics = networkInterface.GetIPStatistics();
                receive += statistics.BytesReceived;
                send += statistics.BytesSent;
            }
        }
        catch
        {
            return (0, 0);
        }

        var elapsed = _previousNetworkAt == default ? 0 : (now - _previousNetworkAt).TotalSeconds;
        var rxRate = elapsed > 0 && receive >= _previousNetworkRx
            ? (receive - _previousNetworkRx) / elapsed
            : 0;
        var txRate = elapsed > 0 && send >= _previousNetworkTx
            ? (send - _previousNetworkTx) / elapsed
            : 0;
        _previousNetworkRx = receive;
        _previousNetworkTx = send;
        _previousNetworkAt = now;
        return (rxRate, txRate);
    }

    private static int CountProcesses()
    {
        try
        {
            var processes = Process.GetProcesses();
            foreach (var process in processes)
            {
                process.Dispose();
            }
            return processes.Length;
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose() => _hardware.Dispose();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(
        out FileTime idleTime,
        out FileTime kernelTime,
        out FileTime userTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
        public readonly ulong ToUInt64() => ((ulong)HighDateTime << 32) | LowDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
