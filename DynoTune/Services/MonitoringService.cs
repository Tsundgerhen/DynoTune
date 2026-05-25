using System;
using System.Collections.Generic;
using System.Linq;
using DynoTune.Models;

namespace DynoTune.Services;

public class MonitoringService
{
    private readonly LibreHardwareMonitorService _cpuMonitoringService;
    private readonly AmdAdlxService _gpuMonitoringService;
    private readonly HwinfoSharedMemoryService _hwinfoService = new();

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
        var fans = new List<FanInfo>(_cpuMonitoringService.GetFanReadings());
        GpuMetrics gpu = _gpuMonitoringService.GetGpuMetrics();

        if (_hwinfoService.TryGetCpuTelemetry(out HwinfoCpuTelemetry hwinfo))
        {
            MergeHwinfoFallback(cpu, hwinfo);
            AppendHwinfoFans(fans, hwinfo);
        }

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

    private static void MergeHwinfoFallback(CpuMetrics cpu, HwinfoCpuTelemetry hwinfo)
    {
        if (!cpu.HasTemperature && hwinfo.HasTemperature)
        {
            cpu.TemperatureC = hwinfo.TemperatureC;
            cpu.HasTemperature = true;
            cpu.TemperatureSource = "HWiNFO";
        }
        if (!cpu.HasClock && hwinfo.HasClock)
        {
            cpu.ClockMHz = hwinfo.ClockMHz!.Value;
            cpu.HasClock = true;
            cpu.ClockSource = "HWiNFO";
        }
        if (!cpu.HasPower && hwinfo.HasPower)
        {
            cpu.PowerW = hwinfo.PackagePowerW;
            cpu.PackagePowerW = hwinfo.PackagePowerW;
            cpu.HasPower = true;
            cpu.PowerSource = "HWiNFO";
        }
        if (!cpu.HasCpuFan && hwinfo.HasFan)
        {
            cpu.CpuFanRpm = hwinfo.CpuFanRpm;
            cpu.CpuFanPercent = hwinfo.CpuFanPercent;
            cpu.HasCpuFan = true;
            cpu.FanSource = "HWiNFO";
        }
    }

    private static void AppendHwinfoFans(List<FanInfo> fans, HwinfoCpuTelemetry hwinfo)
    {
        if (fans.Count == 0 && hwinfo.HasFan)
        {
            fans.Add(new FanInfo
            {
                Name = "CPU Fan",
                Rpm = hwinfo.CpuFanRpm ?? 0,
                SpeedPercent = hwinfo.CpuFanPercent
            });
        }

        foreach (FanInfo f in hwinfo.AdditionalSystemFans)
        {
            if (!fans.Any(x => string.Equals(x.Name, f.Name, StringComparison.OrdinalIgnoreCase)))
                fans.Add(f);
        }
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
