namespace DynoTune.Models;

public class SensorSnapshot
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public CpuMetrics Cpu { get; set; } = new();
    public GpuMetrics Gpu { get; set; } = new();
    public double MemoryUsedGB { get; set; }
    public double MemoryTotalGB { get; set; }
    public double? SystemPowerW { get; set; }
    public double? AmbientTemperatureC { get; set; }

    /// <summary>All fan sensors detected by LibreHardwareMonitor (CPU fan, chassis fans, etc.).</summary>
    public IReadOnlyList<FanInfo> Fans { get; set; } = Array.Empty<FanInfo>();
}