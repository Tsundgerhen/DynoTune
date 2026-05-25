namespace DynoTune.Models;

public class SearchCandidate
{
    public string CandidateId { get; set; } = string.Empty;
    public int Index { get; set; }
    public WorkloadType WorkloadType { get; set; } = WorkloadType.Unknown;
    public WindowsPowerPlanKind PreferredPowerPlan { get; set; } = WindowsPowerPlanKind.Balanced;
    public int? GpuVoltageMv { get; set; }
    public int? GpuMaxClockMHz { get; set; }
    public int? GpuPowerLimitPercent { get; set; }
    public int? CpuMinFrequencyPercent { get; set; }
    public int? CpuMaxFrequencyPercent { get; set; }
    public string Reason { get; set; } = string.Empty;

    /// <summary>Current normalized weight in (0,1]; initialized to 1/N uniformly across candidates.</summary>
    public double Weight { get; set; } = 1.0;

    /// <summary>Voltage safety margin (mV) applied on top of V_candidate when writing to GPU.</summary>
    public int GpuSafetyMarginMv { get; set; }
}
