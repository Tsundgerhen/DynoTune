using DynoTune.Models;

namespace DynoTune.Services;

public class MonitoringService
{
    private readonly LibreHardwareMonitorService _cpuMonitoringService;
    private readonly AmdAdlxService _gpuMonitoringService;

    public MonitoringService(
        LibreHardwareMonitorService cpuMonitoringService,
        AmdAdlxService gpuMonitoringService)
    {
        _cpuMonitoringService = cpuMonitoringService;
        _gpuMonitoringService = gpuMonitoringService;
    }

    public SensorSnapshot GetCurrentSnapshot()
    {
        CpuMetrics cpu = _cpuMonitoringService.GetCpuMetrics();
        IReadOnlyList<FanInfo> fans = _cpuMonitoringService.GetFanReadings();
        GpuMetrics gpu = _gpuMonitoringService.GetGpuMetrics();
        double memoryUsedGb = 0;
        double memoryTotalGb = 0;

        if (TryGetPhysicalMemory(out ulong totalBytes, out ulong availableBytes))
        {
            memoryTotalGb = totalBytes / 1024d / 1024d / 1024d;
            memoryUsedGb = (totalBytes - availableBytes) / 1024d / 1024d / 1024d;
        }

        return new SensorSnapshot
        {
            Timestamp = DateTime.Now,
            Cpu = cpu,
            Gpu = gpu,
            Fans = fans,
            MemoryUsedGB = memoryUsedGb,
            MemoryTotalGB = memoryTotalGb
        };
    }

    // Backward-compatible alias for earlier call sites.
    public SensorSnapshot GetSnapshot()
    {
        return GetCurrentSnapshot();
    }

    private static bool TryGetPhysicalMemory(out ulong totalBytes, out ulong availableBytes)
    {
        totalBytes = 0;
        availableBytes = 0;

        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status))
        {
            return false;
        }

        totalBytes = status.ullTotalPhys;
        availableBytes = status.ullAvailPhys;
        return true;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] MemoryStatusEx lpBuffer);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private sealed class MemoryStatusEx
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

        public MemoryStatusEx()
        {
            dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MemoryStatusEx));
        }
    }
}
