namespace DynoTune.Models;

public class LogRecord
{
    public DateTime Timestamp { get; set; }
    public string ActiveProfile { get; set; } = "Default";

    public WorkloadType WorkloadType { get; set; } = WorkloadType.Unknown;
    public WorkloadClass CoarseWorkloadClass { get; set; } = WorkloadClass.Mixed;
    public string ClassificationReason { get; set; } = string.Empty;

    public double CpuUsagePercent { get; set; }
    public double? CpuTemperatureC { get; set; }
    public double CpuClockMHz { get; set; }
    public double? CpuPowerW { get; set; }

    public string GpuName { get; set; } = string.Empty;
    public double GpuUsagePercent { get; set; }
    public double GpuTemperatureC { get; set; }
    public double GpuCoreClockMHz { get; set; }
    public double GpuMemoryClockMHz { get; set; }
    public double GpuPowerW { get; set; }
    public int GpuFanRpm { get; set; }
    public double? GpuVramUsageMb { get; set; }

    public double MemoryUsedGB { get; set; }
    public double MemoryTotalGB { get; set; }
    public double? SystemPowerW { get; set; }
    public double? AmbientTemperatureC { get; set; }

    public DangerLevel DangerLevel { get; set; } = DangerLevel.Safe;
    public DangerReason DangerReason { get; set; } = DangerReason.None;
    public string DangerReasonDetail { get; set; } = string.Empty;
    public bool DangerRollbackApplied { get; set; }

    public string OptimizerPhase { get; set; } = string.Empty;
    public string OptimizerCandidateName { get; set; } = string.Empty;
    public bool OptimizerCandidateApplied { get; set; }
    public string OptimizerAcceptanceReason { get; set; } = string.Empty;
    public string OptimizerRollbackReason { get; set; } = string.Empty;

    public string SearchSessionId { get; set; } = string.Empty;
    public WorkloadType SearchWorkloadType { get; set; } = WorkloadType.Unknown;
    public string SearchPhase { get; set; } = string.Empty;
    public string SearchCandidateId { get; set; } = string.Empty;
    public int SearchCandidateIndex { get; set; } = -1;
    public bool SearchAccepted { get; set; }
    public string SearchDecision { get; set; } = string.Empty;
    public double? SearchPerfDropPercent { get; set; }
    public double? SearchPowerDeltaPercent { get; set; }

    // Search validation fields (nullable to keep non-search rows clean)
    public int? SearchRequestedGpuVoltageMv { get; set; }
    public int? SearchAppliedGpuVoltageMv { get; set; }
    public int? SearchGpuSafetyMarginMv { get; set; }
    public int? SearchRequestedGpuClockMHz { get; set; }
    public int? SearchRequestedGpuPowerLimitPercent { get; set; }
    public string SearchRequestedPowerPlan { get; set; } = string.Empty;
    public string SearchConfirmedPowerPlan { get; set; } = string.Empty;
    public bool? SearchPowerPlanConfirmed { get; set; }
    public double? SearchCandidateWeight { get; set; }
    public double? SearchLossEnergy { get; set; }
    public double? SearchLossPerf { get; set; }
    public double? SearchLossTotal { get; set; }
    public double? SearchWeightAfterUpdate { get; set; }
    public double? SearchObjectiveScore { get; set; }
    public double? SearchRiskPenalty { get; set; }
    public double? SearchVolatility { get; set; }
    public string SearchVoltageBoundaryUpdate { get; set; } = string.Empty;
    public int? SearchVLastKnownGoodMv { get; set; }
    public int? SearchVFirstFailMv { get; set; }
    public double? SearchBaselineAvgPowerW { get; set; }
    public double? SearchBaselineAvgPerfProxy { get; set; }
    public double? SearchBaselineAvgFanRpm { get; set; }
    public bool? SearchGpuApplySucceeded { get; set; }
    public bool SearchCpuOnlyFallbackUsed { get; set; }
    public string SearchApplyFailureReason { get; set; } = string.Empty;
    public int? SearchCpuOnlyFallbackCount { get; set; }
    public string SearchCandidateDecision { get; set; } = string.Empty;
    public string SearchNextAction { get; set; } = string.Empty;
}
