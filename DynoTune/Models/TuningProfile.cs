namespace DynoTune.Models;

public class TuningProfile
{
    public string Name { get; set; } = string.Empty;
    public WorkloadClass TargetWorkload { get; set; } = WorkloadClass.Mixed;

    /// <summary>Optional fine-grained match for <see cref="WorkloadType"/>; when null, only <see cref="TargetWorkload"/> is used.</summary>
    public WorkloadType? TargetWorkloadType { get; set; }

    public WindowsPowerPlanKind PreferredPowerPlan { get; set; } = WindowsPowerPlanKind.Balanced;

    public int? GpuPowerLimitPercent { get; set; }
    public int? GpuMaxClockMHz { get; set; }
    public int? GpuVoltageMv { get; set; }

    public int? CpuPptW { get; set; }
    public int? CpuTdcA { get; set; }
    public int? CpuEdcA { get; set; }

    public List<FanCurvePoint> GpuFanCurve { get; set; } = new();
    public SafetyLimits SafetyLimits { get; set; } = new();
}
