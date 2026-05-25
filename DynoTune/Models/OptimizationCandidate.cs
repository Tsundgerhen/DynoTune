namespace DynoTune.Models;

public class OptimizationCandidate
{
    public string Name { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public WorkloadType WorkloadType { get; set; } = WorkloadType.Unknown;
    public WindowsPowerPlanKind PreferredPowerPlan { get; set; } = WindowsPowerPlanKind.Balanced;
    public int? GpuPowerLimitPercent { get; set; }
    public int? GpuMaxClockMHz { get; set; }
    public int? GpuVoltageMv { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime RecommendedAtUtc { get; set; } = DateTime.UtcNow;
}
