using System.Runtime.Versioning;
using System.Security.Principal;
using DynoTune.Models;

namespace DynoTune.ViewModels;

/// <summary>
/// Holds the latest live telemetry values. Updated once per timer tick from MainWindow,
/// then fires <see cref="Refreshed"/> so MonitoringPage can redraw in a single pass.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public class MonitoringViewModel
{
    // ── CPU ──────────────────────────────────────────────────────────────────
    public double CpuUsagePercent { get; private set; }
    public double? CpuTemperatureC { get; private set; }
    public double CpuClockMHz { get; private set; }
    public double? CpuPowerW { get; private set; }
    public double? CpuPackagePowerW { get; private set; }
    public double? CpuFanRpm { get; private set; }
    public double? CpuFanPercent { get; private set; }
    public string CpuTemperatureSource { get; private set; } = "Unavailable";
    public string CpuClockSource { get; private set; } = "Unavailable";
    public string CpuPowerSource { get; private set; } = "Unavailable";
    public string CpuFanSource { get; private set; } = "Unavailable";
    public bool CpuHasTemperature { get; private set; }
    public bool CpuHasClock { get; private set; }
    public bool CpuHasPower { get; private set; }
    public bool CpuHasFan { get; private set; }
    public string CpuAvailabilityNote { get; private set; } = string.Empty;
    public bool CpuIsThrottling { get; private set; }

    // ── GPU ──────────────────────────────────────────────────────────────────
    public string GpuName { get; private set; } = string.Empty;
    public double GpuUsagePercent { get; private set; }
    public double GpuTemperatureC { get; private set; }
    public double? GpuHotspotTemperatureC { get; private set; }
    public double GpuCoreClock { get; private set; }
    public double GpuMemoryClock { get; private set; }
    public double? GpuVoltageMv { get; private set; }
    public double GpuPowerW { get; private set; }
    public int GpuFanRpm { get; private set; }
    public double? GpuFanPercent { get; private set; }
    public double? GpuVramUsageMb { get; private set; }
    public bool GpuIsThrottling { get; private set; }

    // ── System ───────────────────────────────────────────────────────────────
    public double MemoryUsedGB { get; private set; }
    public double MemoryTotalGB { get; private set; }
    public double? SystemPowerW { get; private set; }

    // ── Fans (motherboard / SuperIO / CPU header) ─────────────────────────────
    public IReadOnlyList<FanInfo> SystemFans { get; private set; } = Array.Empty<FanInfo>();

    // ── Classification ───────────────────────────────────────────────────────
    public string WorkloadTypeName { get; private set; } = "Unknown";
    public string CoarseClassName { get; private set; } = "Mixed";
    public string ClassificationReason { get; private set; } = string.Empty;

    // ── Power plan ───────────────────────────────────────────────────────────
    public string PowerPlanLabel { get; private set; } = "--";

    // ── Stability ────────────────────────────────────────────────────────────
    public int WheaErrorCount { get; private set; }
    public int GpuResetCount { get; private set; }

    // ── Session ──────────────────────────────────────────────────────────────
    public DateTime SessionStartUtc { get; } = DateTime.UtcNow;

    /// <summary>True when the process has an elevated admin token (needed for most LHM motherboard/CPU sensors).</summary>
    public bool RunningElevated { get; private set; }

    /// <summary>Fired once after every full telemetry batch update.</summary>
    public event EventHandler? Refreshed;

    public void Update(SensorSnapshot snapshot, ClassificationResult classification, string powerPlanLabel)
    {
        RunningElevated = IsProcessElevated();

        CpuUsagePercent = snapshot.Cpu.UsagePercent;
        CpuTemperatureC = snapshot.Cpu.TemperatureC;
        CpuClockMHz = snapshot.Cpu.ClockMHz;
        CpuPowerW = snapshot.Cpu.PowerW;
        CpuPackagePowerW = snapshot.Cpu.PackagePowerW;
        CpuFanRpm = snapshot.Cpu.CpuFanRpm;
        CpuFanPercent = snapshot.Cpu.CpuFanPercent;
        CpuTemperatureSource = snapshot.Cpu.TemperatureSource;
        CpuClockSource = snapshot.Cpu.ClockSource;
        CpuPowerSource = snapshot.Cpu.PowerSource;
        CpuFanSource = snapshot.Cpu.FanSource;
        CpuHasTemperature = snapshot.Cpu.HasTemperature;
        CpuHasClock = snapshot.Cpu.HasClock;
        CpuHasPower = snapshot.Cpu.HasPower;
        CpuHasFan = snapshot.Cpu.HasCpuFan;
        CpuAvailabilityNote = snapshot.Cpu.AvailabilityNote;
        CpuIsThrottling = snapshot.Cpu.IsThermallyThrottling || snapshot.Cpu.IsPowerThrottling;

        GpuName = snapshot.Gpu.Name;
        GpuUsagePercent = snapshot.Gpu.UsagePercent;
        GpuTemperatureC = snapshot.Gpu.TemperatureC;
        GpuHotspotTemperatureC = snapshot.Gpu.HotspotTemperatureC;
        GpuCoreClock = snapshot.Gpu.CoreClockMHz;
        GpuMemoryClock = snapshot.Gpu.MemoryClockMHz;
        GpuVoltageMv = snapshot.Gpu.VoltageMv;
        GpuPowerW = snapshot.Gpu.PowerW;
        GpuFanRpm = snapshot.Gpu.FanRpm;
        GpuFanPercent = snapshot.Gpu.FanPercent;
        GpuVramUsageMb = snapshot.Gpu.VramUsageMb;
        GpuIsThrottling = snapshot.Gpu.IsThrottling;

        MemoryUsedGB = snapshot.MemoryUsedGB;
        MemoryTotalGB = snapshot.MemoryTotalGB;
        SystemPowerW = snapshot.SystemPowerW;

        SystemFans = snapshot.Fans;

        WorkloadTypeName = classification.WorkloadType.ToString();
        CoarseClassName = classification.CoarseWorkloadClass.ToString();
        ClassificationReason = classification.Reason;

        PowerPlanLabel = powerPlanLabel;

        Refreshed?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateStability(int wheaErrors, int gpuResets)
    {
        WheaErrorCount = wheaErrors;
        GpuResetCount = gpuResets;
        Refreshed?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsProcessElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
