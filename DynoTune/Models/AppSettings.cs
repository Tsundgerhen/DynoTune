namespace DynoTune.Models;

public class AppSettings
{
    // Safety limits applied globally (override per-profile defaults)
    public double MaxCpuTemperatureC { get; set; } = 90.0;
    public double MaxGpuTemperatureC { get; set; } = 85.0;
    public double MaxGpuHotspotTemperatureC { get; set; } = 100.0;

    // Search algorithm parameters
    public double MaxPerfLossPercent { get; set; } = 5.0;
    public int MaxSearchCandidates { get; set; } = 5;
    public OptimizationTargetMode TargetMode { get; set; } = OptimizationTargetMode.Balanced;

    // Sampling and test timing
    public int SamplingIntervalMs { get; set; } = 1000;
    public int ShortTestDurationSec { get; set; } = 15;
    public int LongTestDurationSec { get; set; } = 25;
}
