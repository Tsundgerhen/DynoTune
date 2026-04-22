namespace DynoTune.Models;

public class ExperimentRunResult
{
    public Guid RunId { get; set; } = Guid.NewGuid();
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime EndedAtUtc { get; set; }

    public string WorkloadName { get; set; } = string.Empty;
    public WorkloadClass WorkloadClass { get; set; } = WorkloadClass.Mixed;
    public WorkloadType DominantWorkloadType { get; set; } = WorkloadType.Unknown;
    public ExperimentProfileType ProfileType { get; set; } = ExperimentProfileType.Baseline;

    /// <summary>Optional: WHEA / GPU reset counts observed from the System log during the run window.</summary>
    public StabilitySnapshot? SessionStability { get; set; }

    public PerformanceMetrics Performance { get; set; } = new();
    public EnergyMetrics Energy { get; set; } = new();
    public ThermalMetrics Thermals { get; set; } = new();
    public NoiseMetrics Noise { get; set; } = new();
    public StabilityMetrics Stability { get; set; } = new();
}
