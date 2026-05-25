namespace DynoTune.Models;

public class OptimizationSessionState
{
    public bool IsRunning { get; set; }
    public OptimizationPhase Phase { get; set; } = OptimizationPhase.Idle;
    public WorkloadType ActiveWorkloadType { get; set; } = WorkloadType.Unknown;
    public string BaselineProfileName { get; set; } = string.Empty;
    public OptimizationCandidate? RecommendedCandidate { get; set; }
    public OptimizationResult? LastResult { get; set; }
    public bool AutoApplyEnabled { get; set; }
    public int AutoApplyAfterAcceptedCount { get; set; } = 2;
    public int AcceptedRecommendations { get; set; }
    public string LastDecision { get; set; } = string.Empty;

    /// <summary>Set by Tick() when workload is stable and has no auto-generated profile.</summary>
    public bool ShouldTriggerSearch { get; set; }

    /// <summary>True when the last search was auto-triggered; false when manually started.</summary>
    public bool LastSearchWasAutoTriggered { get; set; }
}
