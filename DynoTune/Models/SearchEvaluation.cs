namespace DynoTune.Models;

public class SearchEvaluation
{
    public string CandidateId { get; set; } = string.Empty;
    public bool Accepted { get; set; }
    public string RejectionReason { get; set; } = string.Empty;
    public double AveragePowerW { get; set; }
    public double AverageFanRpm { get; set; }
    public double PerformanceProxyScore { get; set; }
    public double PowerDeltaPercentVsBaseline { get; set; }
    public double PerfDropPercentVsBaseline { get; set; }

    // Ch5 loss breakdown fields
    /// <summary>L_e component: energy wasted (utilization below reference).</summary>
    public double LossEnergy { get; set; }

    /// <summary>L_p component: performance degraded (utilization above reference).</summary>
    public double LossPerf { get; set; }

    /// <summary>L_total after combining core and memory dimensions via phi.</summary>
    public double LossTotal { get; set; }

    /// <summary>Snapshot of the candidate's normalized weight after this trial's update.</summary>
    public double WeightAfterUpdate { get; set; }

    /// <summary>J_k objective score for accepted candidates (lower is better).</summary>
    public double ObjectiveScore { get; set; }

    /// <summary>Describes any change to VLastKnownGood or VFirstFail after this evaluation.</summary>
    public string VoltageBoundaryUpdate { get; set; } = string.Empty;
}
