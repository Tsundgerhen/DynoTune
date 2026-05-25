namespace DynoTune.Models;

public enum WorkloadSearchPhase
{
    Idle = 0,
    CapturingBaseline = 1,
    ApplyingCandidate = 2,
    CapturingTrial = 3,
    Completed = 4,
    RolledBack = 5,
    Stopped = 6
}

public class WorkloadSearchState
{
    public bool IsRunning { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public SearchObjective Objective { get; set; } = SearchObjective.LowestPowerWithPerfFloor;
    public WorkloadType ActiveWorkloadType { get; set; } = WorkloadType.Unknown;
    public WorkloadSearchPhase Phase { get; set; } = WorkloadSearchPhase.Idle;
    public int CandidateIndex { get; set; } = -1;
    public SearchCandidate? CurrentCandidate { get; set; }
    public SearchCandidate? BestCandidate { get; set; }
    public SearchEvaluation? BestEvaluation { get; set; }
    public string LastDecision { get; set; } = string.Empty;
    public string BaselineProfileName { get; set; } = string.Empty;
    public WindowsPowerPlanKind BaselinePowerPlan { get; set; } = WindowsPowerPlanKind.Balanced;
    public int? BaselineGpuVoltageMv { get; set; }
    public int? BaselineGpuCoreClockMHz { get; set; }
    public int? BaselineGpuPowerLimitPercent { get; set; }
    public WindowsPowerPlanKind CurrentPowerPlan { get; set; } = WindowsPowerPlanKind.Balanced;
    public int? CurrentGpuVoltageMv { get; set; }
    public int? CurrentGpuCoreClockMHz { get; set; }
    public int? CurrentGpuPowerLimitPercent { get; set; }
    public double? LatestPerfDropPercent { get; set; }
    public double? LatestPowerDeltaPercent { get; set; }
    public List<SearchCandidate> Candidates { get; } = new();
    public List<SearchEvaluation> Evaluations { get; } = new();

    // Voltage boundary tracking (per-session, per-workload)
    /// <summary>Highest voltage (mV) that has been confirmed stable this session.</summary>
    public int? VLastKnownGoodMv { get; set; }

    /// <summary>Lowest voltage (mV) that produced instability this session.</summary>
    public int? VFirstFailMv { get; set; }

    // Baseline window averages used in loss formulas
    public double BaselineAvgPowerW { get; set; }
    public double BaselineAvgPerfProxy { get; set; }
    public double BaselineAvgFanRpm { get; set; }

    // Last apply/evaluation diagnostics for logging and validation
    public int? LastRequestedGpuVoltageMv { get; set; }
    public int? LastAppliedGpuVoltageMv { get; set; }
    public int? LastGpuSafetyMarginMv { get; set; }
    public int? LastRequestedGpuClockMHz { get; set; }
    public int? LastRequestedGpuPowerLimitPercent { get; set; }
    public WindowsPowerPlanKind? LastRequestedPowerPlan { get; set; }
    public WindowsPowerPlanKind? LastConfirmedPowerPlan { get; set; }
    public bool? LastPowerPlanConfirmed { get; set; }
    public double? LastRiskPenalty { get; set; }
    public double? LastVolatility { get; set; }

    // Candidate-apply diagnostic state (durable for logging)
    public bool? LastGpuApplySucceeded { get; set; }
    public bool LastCpuOnlyFallbackUsed { get; set; }
    public string LastApplyFailureReason { get; set; } = string.Empty;
    public int CpuOnlyFallbackCount { get; set; }

    // Keep candidate decision separate from transition text to avoid overwrites in logs.
    public string LastCandidateDecision { get; set; } = string.Empty;
    public string LastNextAction { get; set; } = string.Empty;
}
