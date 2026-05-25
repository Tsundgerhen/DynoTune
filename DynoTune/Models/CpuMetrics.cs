namespace DynoTune.Models;

public class CpuMetrics
{
    public string Name { get; set; } = string.Empty;
    public double UsagePercent { get; set; }
    public double? TemperatureC { get; set; }
    public double ClockMHz { get; set; }
    public double? PowerW { get; set; }
    public double? PackagePowerW { get; set; }
    public double? CpuFanRpm { get; set; }
    public double? CpuFanPercent { get; set; }

    // Data-source labels used by UI to clarify fallback paths.
    public string TemperatureSource { get; set; } = "Unavailable";
    public string ClockSource { get; set; } = "Unavailable";
    public string PowerSource { get; set; } = "Unavailable";
    public string FanSource { get; set; } = "Unavailable";

    // Availability flags for explicit unsupported rendering.
    public bool HasTemperature { get; set; }
    public bool HasClock { get; set; }
    public bool HasPower { get; set; }
    public bool HasCpuFan { get; set; }
    public string AvailabilityNote { get; set; } = string.Empty;
    public bool IsThermallyThrottling { get; set; }
    public bool IsPowerThrottling { get; set; }
}