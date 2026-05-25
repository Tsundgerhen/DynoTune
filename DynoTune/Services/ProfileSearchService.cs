using System.Linq;
using DynoTune.Models;

namespace DynoTune.Services;

public class ProfileSearchService
{
    // Sampling window sizes (computed from AppSettings at runtime)
    private int BaselineSamplesTarget => Math.Max(10,
        _settings.Current.LongTestDurationSec * 1000 / Math.Max(500, _settings.Current.SamplingIntervalMs));
    private int TrialSamplesTarget => Math.Max(5,
        _settings.Current.ShortTestDurationSec * 1000 / Math.Max(500, _settings.Current.SamplingIntervalMs));
    private const int StabilizationTicks = 5;

    // Performance floor (from AppSettings)
    private double MaxPerfDropPercent => _settings.Current.MaxPerfLossPercent;

    // J_k objective scoring lambdas
    private const double Lambda_p = 1.0;    // power delta weight
    private const double Lambda_n = 0.5;    // fan noise weight
    private const double Lambda_t = 0.3;    // temperature weight
    private const double Lambda_q = 2.0;    // performance drop penalty weight
    private const double Lambda_r = 1.5;    // risk penalty weight

    private const double Eps = 1e-6;

    // Anti-oscillation guard
    private const int CooldownTicks = 20;
    private int _lastSwitchTickCount;
    private int _totalTickCount;

    private readonly AppSettingsService _settings;
    private readonly ProfileService _profileService;
    private readonly WindowsPowerPlanService _powerPlanService;
    private readonly AmdAdlxService _gpuService;
    private readonly List<SensorSnapshot> _baselineSamples = new();
    private readonly List<SensorSnapshot> _trialSamples = new();
    private int _stabilizationTicksRemaining;

    public ProfileSearchService(
        ProfileService profileService,
        WindowsPowerPlanService powerPlanService,
        AmdAdlxService gpuService,
        AppSettingsService settings)
    {
        _profileService = profileService;
        _powerPlanService = powerPlanService;
        _gpuService = gpuService;
        _settings = settings;
    }

    public WorkloadSearchState State { get; } = new();

    public void Start(SearchObjective objective)
    {
        State.IsRunning = true;
        State.SessionId = Guid.NewGuid().ToString("N");
        State.Objective = objective;
        State.ActiveWorkloadType = WorkloadType.Unknown;
        State.Phase = WorkloadSearchPhase.CapturingBaseline;
        State.CandidateIndex = -1;
        State.CurrentCandidate = null;
        State.BestCandidate = null;
        State.BestEvaluation = null;
        State.BaselineProfileName = string.Empty;
        State.BaselinePowerPlan = WindowsPowerPlanKind.Balanced;
        State.BaselineGpuVoltageMv = null;
        State.BaselineGpuCoreClockMHz = null;
        State.BaselineGpuPowerLimitPercent = null;
        State.CurrentPowerPlan = WindowsPowerPlanKind.Balanced;
        State.CurrentGpuVoltageMv = null;
        State.CurrentGpuCoreClockMHz = null;
        State.CurrentGpuPowerLimitPercent = null;
        State.LatestPerfDropPercent = null;
        State.LatestPowerDeltaPercent = null;
        State.VLastKnownGoodMv = null;
        State.VFirstFailMv = null;
        State.BaselineAvgPowerW = 0;
        State.BaselineAvgPerfProxy = 0;
        State.BaselineAvgFanRpm = 0;
        State.LastRequestedGpuVoltageMv = null;
        State.LastAppliedGpuVoltageMv = null;
        State.LastGpuSafetyMarginMv = null;
        State.LastRequestedGpuClockMHz = null;
        State.LastRequestedGpuPowerLimitPercent = null;
        State.LastRequestedPowerPlan = null;
        State.LastConfirmedPowerPlan = null;
        State.LastPowerPlanConfirmed = null;
        State.LastRiskPenalty = null;
        State.LastVolatility = null;
        State.LastGpuApplySucceeded = null;
        State.LastCpuOnlyFallbackUsed = false;
        State.LastApplyFailureReason = string.Empty;
        State.CpuOnlyFallbackCount = 0;
        State.LastCandidateDecision = string.Empty;
        State.LastNextAction = string.Empty;
        State.LastDecision = "Search started. Capturing baseline.";
        State.Candidates.Clear();
        State.Evaluations.Clear();
        _baselineSamples.Clear();
        _trialSamples.Clear();
        _stabilizationTicksRemaining = 0;
        _lastSwitchTickCount = 0;
        _totalTickCount = 0;
    }

    public void Stop(string reason = "Search stopped by user.")
    {
        State.IsRunning = false;
        State.Phase = WorkloadSearchPhase.Stopped;
        State.LastNextAction = "Stopped";
        State.LastDecision = reason;
    }

    public void Tick(SensorSnapshot snapshot, ClassificationResult classification, DangerState dangerState)
    {
        if (!State.IsRunning)
        {
            return;
        }

        _totalTickCount++;

        if (dangerState.Level == DangerLevel.Critical)
        {
            RollbackAndStop($"Critical danger during search: {dangerState.Reason}");
            return;
        }

        if (classification.WorkloadType == WorkloadType.Unknown)
        {
            State.LastDecision = "Waiting for known workload classification.";
            return;
        }

        if (State.ActiveWorkloadType == WorkloadType.Unknown)
        {
            State.ActiveWorkloadType = classification.WorkloadType;
        }

        if (classification.WorkloadType != State.ActiveWorkloadType)
        {
            State.LastDecision = $"Holding search on {State.ActiveWorkloadType}; current workload is {classification.WorkloadType}.";
            return;
        }

        switch (State.Phase)
        {
            case WorkloadSearchPhase.CapturingBaseline:
                CaptureBaseline(snapshot);
                break;
            case WorkloadSearchPhase.ApplyingCandidate:
                ApplyCurrentCandidate();
                break;
            case WorkloadSearchPhase.CapturingTrial:
                CaptureTrial(snapshot);
                break;
            case WorkloadSearchPhase.Completed:
            case WorkloadSearchPhase.RolledBack:
            case WorkloadSearchPhase.Stopped:
            case WorkloadSearchPhase.Idle:
            default:
                break;
        }
    }

    public string BuildSummaryReport()
    {
        if (State.Evaluations.Count == 0)
        {
            return "No profile-search evaluations were recorded.";
        }

        int totalTrials = State.Evaluations.Count;
        int acceptedTrials = State.Evaluations.Count(e => e.Accepted);
        int rejectedTrials = totalTrials - acceptedTrials;
        string rejectionSummary = string.Join(
            " | ",
            State.Evaluations
                .Where(e => !e.Accepted && !string.IsNullOrWhiteSpace(e.RejectionReason))
                .GroupBy(e => e.RejectionReason)
                .Select(g => $"{g.Key}:{g.Count()}"));

        if (string.IsNullOrWhiteSpace(rejectionSummary))
        {
            rejectionSummary = "none";
        }

        SearchEvaluation? best = State.BestEvaluation;
        if (best is null || State.BestCandidate is null)
        {
            return $"Session={State.SessionId}, Workload={State.ActiveWorkloadType}, " +
                   $"Trials={totalTrials}, Accepted={acceptedTrials}, Rejected={rejectedTrials}, " +
                   $"CpuOnlyFallbacks={State.CpuOnlyFallbackCount}, RejectReasons={rejectionSummary}. " +
                   "Profile search finished without an accepted candidate.";
        }

        return $"Session={State.SessionId}, Workload={State.ActiveWorkloadType}, " +
               $"Trials={totalTrials}, Accepted={acceptedTrials}, Rejected={rejectedTrials}, " +
               $"CpuOnlyFallbacks={State.CpuOnlyFallbackCount}, " +
               $"Best={State.BestCandidate.CandidateId}, PowerDelta={best.PowerDeltaPercentVsBaseline:F2}%, " +
               $"PerfDrop={best.PerfDropPercentVsBaseline:F2}%, J_k={best.ObjectiveScore:F3}, " +
               $"RejectReasons={rejectionSummary}";
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Baseline capture
    // ─────────────────────────────────────────────────────────────────────────

    private void CaptureBaseline(SensorSnapshot snapshot)
    {
        _baselineSamples.Add(snapshot);
        State.LastDecision = $"Capturing baseline {_baselineSamples.Count}/{BaselineSamplesTarget}.";

        if (_baselineSamples.Count < BaselineSamplesTarget)
        {
            return;
        }

        // Store baseline averages for trial evaluation
        State.BaselineAvgPowerW = ComputeAveragePower(_baselineSamples);
        State.BaselineAvgPerfProxy = ComputePerformanceProxy(_baselineSamples);
        State.BaselineAvgFanRpm = ComputeAverageFan(_baselineSamples);

        SensorSnapshot lastSample = _baselineSamples[^1];
        TuningProfile vendorBaseline = _profileService.GetVendorSafeBaselineProfile() ?? _profileService.GetSafeFallbackProfile();
        State.BaselineProfileName = vendorBaseline.Name;
        State.BaselinePowerPlan = vendorBaseline.PreferredPowerPlan;
        State.BaselineGpuVoltageMv = lastSample.Gpu.VoltageMv > 0 ? (int)Math.Round(lastSample.Gpu.VoltageMv.Value) : null;
        State.BaselineGpuCoreClockMHz = lastSample.Gpu.CoreClockMHz > 0 ? (int)Math.Round(lastSample.Gpu.CoreClockMHz) : null;
        State.BaselineGpuPowerLimitPercent = vendorBaseline.GpuPowerLimitPercent;

        // Build random candidates for this workload
        State.Candidates.Clear();
        foreach (SearchCandidate candidate in BuildCandidates(lastSample, State.ActiveWorkloadType))
        {
            State.Candidates.Add(candidate);
        }

        if (State.Candidates.Count == 0)
        {
            RollbackAndStop("No valid search candidates generated.");
            return;
        }

        // Start with the first candidate (sequential order)
        int firstIdx = SelectNextCandidateIndex();
        State.CandidateIndex = firstIdx;
        State.CurrentCandidate = State.Candidates[firstIdx];
        SyncCurrentCandidateFields(State.CurrentCandidate);
        State.Phase = WorkloadSearchPhase.ApplyingCandidate;
        _lastSwitchTickCount = _totalTickCount;
        State.LastNextAction = $"Applying candidate {State.CurrentCandidate.CandidateId}.";
        State.LastDecision = $"Baseline captured. {State.LastNextAction}";
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Candidate apply with CPU plan confirmation
    // ─────────────────────────────────────────────────────────────────────────

    private void ApplyCurrentCandidate()
    {
        SearchCandidate? candidate = State.CurrentCandidate;
        if (candidate is null)
        {
            CompleteSearch("No candidate to apply.");
            return;
        }

        // Anti-oscillation: enforce cooldown before switching
        if (_totalTickCount - _lastSwitchTickCount < CooldownTicks && State.CandidateIndex > 0)
        {
            State.LastDecision = $"Cooldown: waiting {CooldownTicks - (_totalTickCount - _lastSwitchTickCount)} ticks before applying {candidate.CandidateId}.";
            return;
        }

        // Apply CPU power plan
        State.LastRequestedPowerPlan = candidate.PreferredPowerPlan;
        bool planSet = _powerPlanService.TrySetActivePlan(candidate.PreferredPowerPlan);
        if (!planSet)
        {
            State.LastPowerPlanConfirmed = false;
            RejectCurrentCandidate($"Failed to set Windows power plan to {candidate.PreferredPowerPlan}.");
            return;
        }

        // Verify plan is confirmed active
        bool planConfirmed = _powerPlanService.TryGetActivePlanKind(out WindowsPowerPlanKind activePlan)
                             && activePlan == candidate.PreferredPowerPlan;
        State.LastConfirmedPowerPlan = activePlan;
        State.LastPowerPlanConfirmed = planConfirmed;
        if (!planConfirmed)
        {
            RejectCurrentCandidate($"CPU power plan apply not confirmed: expected {candidate.PreferredPowerPlan}, got {activePlan}.");
            return;
        }

        // Apply CPU frequency settings from the candidate
        if (candidate.CpuMinFrequencyPercent.HasValue)
            _powerPlanService.TrySetCpuMinFrequencyPercent(candidate.CpuMinFrequencyPercent.Value);
        if (candidate.CpuMaxFrequencyPercent.HasValue)
            _powerPlanService.TrySetCpuMaxFrequencyPercent(candidate.CpuMaxFrequencyPercent.Value);

        // Apply V_apply = V_candidate + GpuSafetyMarginMv
        int? vApply = candidate.GpuVoltageMv.HasValue
            ? candidate.GpuVoltageMv.Value + candidate.GpuSafetyMarginMv
            : null;
        State.LastRequestedGpuVoltageMv = candidate.GpuVoltageMv;
        State.LastAppliedGpuVoltageMv = vApply;
        State.LastGpuSafetyMarginMv = candidate.GpuSafetyMarginMv;
        State.LastRequestedGpuClockMHz = candidate.GpuMaxClockMHz;
        State.LastRequestedGpuPowerLimitPercent = candidate.GpuPowerLimitPercent;
        State.LastApplyFailureReason = string.Empty;
        State.LastCpuOnlyFallbackUsed = false;

        bool gpuOk = _gpuService.TryApplyUndervoltCandidate(
            vApply,
            candidate.GpuMaxClockMHz,
            candidate.GpuPowerLimitPercent,
            out string gpuReason);
        State.LastGpuApplySucceeded = gpuOk;

        if (!gpuOk)
        {
            // CPU-only fallback: power plan succeeded, GPU failed — continue trial with CPU settings only
            State.LastCpuOnlyFallbackUsed = true;
            State.CpuOnlyFallbackCount++;
            State.LastApplyFailureReason = gpuReason;
            State.LastCandidateDecision = $"Candidate {candidate.CandidateId}: GPU apply failed; using CPU-only fallback. Reason: {gpuReason}";
        }
        else
        {
            State.LastCandidateDecision = $"Candidate {candidate.CandidateId}: GPU+CPU apply succeeded.";
        }

        _trialSamples.Clear();
        _stabilizationTicksRemaining = StabilizationTicks;
        _lastSwitchTickCount = _totalTickCount;
        State.Phase = WorkloadSearchPhase.CapturingTrial;
        State.LastNextAction = $"Stabilizing candidate {candidate.CandidateId}.";
        State.LastDecision = $"Candidate applied: {candidate.CandidateId} (V_apply={vApply} mV, plan={activePlan}, cpuOnlyFallback={State.LastCpuOnlyFallbackUsed}).";
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Trial capture and evaluation
    // ─────────────────────────────────────────────────────────────────────────

    private void CaptureTrial(SensorSnapshot snapshot)
    {
        if (_stabilizationTicksRemaining > 0)
        {
            _stabilizationTicksRemaining--;
            return;
        }

        _trialSamples.Add(snapshot);
        State.LastDecision = $"Evaluating {State.CurrentCandidate?.CandidateId} sample {_trialSamples.Count}/{TrialSamplesTarget}.";
        if (_trialSamples.Count < TrialSamplesTarget)
        {
            return;
        }

        SearchEvaluation evaluation = EvaluateCurrentCandidate();
        State.LatestPerfDropPercent = evaluation.PerfDropPercentVsBaseline;
        State.LatestPowerDeltaPercent = evaluation.PowerDeltaPercentVsBaseline;
        State.Evaluations.Add(evaluation);

        if (evaluation.Accepted)
        {
            // Update best by minimum J_k (objective score)
            if (State.BestEvaluation is null || evaluation.ObjectiveScore < State.BestEvaluation.ObjectiveScore)
            {
                State.BestEvaluation = evaluation;
                State.BestCandidate = State.CurrentCandidate;
            }
            State.LastCandidateDecision = $"Accepted candidate {evaluation.CandidateId} (J_k={evaluation.ObjectiveScore:F3}).";
        }
        else
        {
            State.LastCandidateDecision = $"Rejected candidate {evaluation.CandidateId}: {evaluation.RejectionReason}";
        }

        MoveToNextCandidateOrComplete();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Evaluation and J_k scoring
    // ─────────────────────────────────────────────────────────────────────────

    private SearchEvaluation EvaluateCurrentCandidate()
    {
        SearchCandidate candidate = State.CurrentCandidate ?? new SearchCandidate { CandidateId = "none" };

        double trialPower = ComputeAveragePower(_trialSamples);
        double trialPerf = ComputePerformanceProxy(_trialSamples);
        double trialFan = ComputeAverageFan(_trialSamples);
        double trialTemp = ComputeAverageGpuTemp(_trialSamples);
        double baselineTemp = ComputeAverageGpuTemp(_baselineSamples);

        double baselinePower = State.BaselineAvgPowerW > Eps ? State.BaselineAvgPowerW : ComputeAveragePower(_baselineSamples);
        double baselinePerf = State.BaselineAvgPerfProxy > Eps ? State.BaselineAvgPerfProxy : ComputePerformanceProxy(_baselineSamples);
        double baselineFan = State.BaselineAvgFanRpm > Eps ? State.BaselineAvgFanRpm : ComputeAverageFan(_baselineSamples);

        double perfDropPercent = baselinePerf <= Eps
            ? 0
            : Math.Max(0, ((baselinePerf - trialPerf) / baselinePerf) * 100.0);
        double powerDeltaPercent = baselinePower <= Eps
            ? 0
            : ((trialPower - baselinePower) / baselinePower) * 100.0;

        // Performance floor check (acceptance gate)
        bool accepted = perfDropPercent <= MaxPerfDropPercent;
        string rejectionReason = accepted
            ? string.Empty
            : $"Performance drop {perfDropPercent:F2}% exceeds {MaxPerfDropPercent:F2}% limit.";
        if (!accepted && State.LastCpuOnlyFallbackUsed && !string.IsNullOrWhiteSpace(State.LastApplyFailureReason))
        {
            rejectionReason = $"{rejectionReason} CPU-only fallback used ({State.LastApplyFailureReason}).";
        }

        var evaluation = new SearchEvaluation
        {
            CandidateId = candidate.CandidateId,
            Accepted = accepted,
            RejectionReason = rejectionReason,
            AveragePowerW = trialPower,
            AverageFanRpm = trialFan,
            PerformanceProxyScore = trialPerf,
            PowerDeltaPercentVsBaseline = powerDeltaPercent,
            PerfDropPercentVsBaseline = perfDropPercent,
            WeightAfterUpdate = candidate.Weight   // weights are uniform; record as-is
        };

        // Compute J_k for accepted candidates (lower = better)
        if (accepted)
        {
            double fanNoiseDelta = baselineFan > Eps ? (trialFan - baselineFan) / baselineFan * 100.0 : 0;
            double tempDelta = trialTemp - baselineTemp;
            double volatility = ComputePerformanceVolatility(_trialSamples);
            double riskPenalty = volatility > 20.0 ? 0.5 : 0;
            State.LastRiskPenalty = riskPenalty;
            State.LastVolatility = volatility;

            evaluation.ObjectiveScore = ComputeObjectiveScore(
                powerDeltaPercent, fanNoiseDelta, tempDelta, perfDropPercent, riskPenalty);
        }
        else
        {
            State.LastRiskPenalty = null;
            State.LastVolatility = null;
        }

        return evaluation;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  J_k objective scoring
    // ─────────────────────────────────────────────────────────────────────────

    private static double ComputeObjectiveScore(
        double powerDeltaPercent,
        double fanNoiseDelta,
        double tempDelta,
        double perfDropPercent,
        double riskPenalty)
    {
        return Lambda_p * powerDeltaPercent
             + Lambda_n * fanNoiseDelta
             + Lambda_t * Math.Max(0, tempDelta)
             + Lambda_q * perfDropPercent
             + Lambda_r * riskPenalty;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Candidate selection: sequential scan (first untried wins)
    // ─────────────────────────────────────────────────────────────────────────

    private int SelectNextCandidateIndex()
    {
        var tried = State.Evaluations.Select(e => e.CandidateId).ToHashSet();
        for (int i = 0; i < State.Candidates.Count; i++)
        {
            if (!tried.Contains(State.Candidates[i].CandidateId))
                return i;
        }
        return 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Reject / move / complete / rollback
    // ─────────────────────────────────────────────────────────────────────────

    private void RejectCurrentCandidate(string reason)
    {
        SearchCandidate? candidate = State.CurrentCandidate;
        if (candidate is null)
        {
            CompleteSearch("Search candidate became unavailable.");
            return;
        }

        State.Evaluations.Add(new SearchEvaluation
        {
            CandidateId = candidate.CandidateId,
            Accepted = false,
            RejectionReason = reason,
            WeightAfterUpdate = candidate.Weight
        });
        State.LatestPerfDropPercent = null;
        State.LatestPowerDeltaPercent = null;
        State.LastCandidateDecision = $"Rejected candidate {candidate.CandidateId}: {reason}";
        State.LastDecision = State.LastCandidateDecision;
        MoveToNextCandidateOrComplete();
    }

    private void MoveToNextCandidateOrComplete()
    {
        var tried = State.Evaluations.Select(e => e.CandidateId).ToHashSet();
        bool anyUntried = State.Candidates.Any(c => !tried.Contains(c.CandidateId));

        if (!anyUntried)
        {
            CompleteSearch("All candidates evaluated.");
            return;
        }

        int nextIdx = SelectNextCandidateIndex();
        State.CandidateIndex = nextIdx;
        State.CurrentCandidate = State.Candidates[nextIdx];
        SyncCurrentCandidateFields(State.CurrentCandidate);
        State.Phase = WorkloadSearchPhase.ApplyingCandidate;
        State.LastNextAction = $"Applying next candidate {State.CurrentCandidate.CandidateId}.";
        State.LastDecision = $"{State.LastCandidateDecision} {State.LastNextAction}".Trim();
    }

    private void CompleteSearch(string reason)
    {
        State.IsRunning = false;
        State.Phase = WorkloadSearchPhase.Completed;

        if (State.BestCandidate is not null)
        {
            PersistBestCandidate(State.BestCandidate);
            SyncCurrentCandidateFields(State.BestCandidate);
            State.LastNextAction = "Completed";
            State.LastCandidateDecision = $"Selected best candidate {State.BestCandidate.CandidateId}.";
            State.LastDecision = $"Search completed. Selected {State.BestCandidate.CandidateId} (J_k={State.BestEvaluation?.ObjectiveScore:F3}).";
            return;
        }

        State.LastNextAction = "Completed";
        State.LastCandidateDecision = "No accepted candidate.";
        State.LastDecision = $"{reason} No accepted candidate.";
    }

    private void PersistBestCandidate(SearchCandidate best)
    {
        TuningProfile baseline = _profileService.GetVendorSafeBaselineProfile() ?? _profileService.GetSafeFallbackProfile();
        var profile = new TuningProfile
        {
            Name = $"AutoSearch {best.WorkloadType}",
            IsAutoGenerated = true,
            ParentProfileName = baseline.Name,
            TargetWorkloadType = best.WorkloadType,
            TargetWorkload = MapToWorkloadClass(best.WorkloadType),
            PreferredPowerPlan = best.PreferredPowerPlan,
            GpuVoltageMv = best.GpuVoltageMv,
            GpuMaxClockMHz = best.GpuMaxClockMHz,
            GpuPowerLimitPercent = best.GpuPowerLimitPercent,
            CpuMinFrequencyPercent = best.CpuMinFrequencyPercent,
            CpuMaxFrequencyPercent = best.CpuMaxFrequencyPercent
        };

        _profileService.UpsertProfile(profile);
        _profileService.SetActiveProfile(profile.Name);
        _profileService.TryApplyPowerPlan(profile);
    }

    private void RollbackAndStop(string reason)
    {
        TuningProfile safe = _profileService.GetSafeFallbackProfile();
        _profileService.TryApplyPowerPlan(safe);
        State.IsRunning = false;
        State.Phase = WorkloadSearchPhase.RolledBack;
        State.CurrentCandidate = null;
        State.CurrentGpuVoltageMv = State.BaselineGpuVoltageMv;
        State.CurrentGpuCoreClockMHz = State.BaselineGpuCoreClockMHz;
        State.CurrentGpuPowerLimitPercent = State.BaselineGpuPowerLimitPercent;
        State.CurrentPowerPlan = State.BaselinePowerPlan;
        State.LastNextAction = "Rolled back";
        State.LastCandidateDecision = reason;
        State.LastDecision = reason;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Random candidate generation within realistic hardware bounds
    // ─────────────────────────────────────────────────────────────────────────

    private IReadOnlyList<SearchCandidate> BuildCandidates(SensorSnapshot baseline, WorkloadType workload)
    {
        var candidates = new List<SearchCandidate>();
        int baseMv = baseline.Gpu.VoltageMv > 0 ? (int)Math.Round(baseline.Gpu.VoltageMv.Value) : 0;
        int count = Math.Max(3, _settings.Current.MaxSearchCandidates);
        double initWeight = 1.0 / (count + 1);

        // Candidate 0: baseline reference — verifies baseline measurement; no settings change
        candidates.Add(new SearchCandidate
        {
            CandidateId = "baseline-ref",
            Index = 0,
            WorkloadType = workload,
            PreferredPowerPlan = GetDefaultPlanForWorkload(workload),
            GpuVoltageMv = baseMv > 0 ? baseMv : null,
            GpuSafetyMarginMv = 0,
            Weight = initWeight,
            Reason = "Baseline reference."
        });

        // Candidates 1..N: random exploration within realistic bounds
        for (int i = 0; i < count; i++)
        {
            candidates.Add(GenerateRandomCandidate(workload, baseMv, i + 1, initWeight));
        }

        return candidates;
    }

    private static SearchCandidate GenerateRandomCandidate(
        WorkloadType workload, int baseMv, int index, double weight)
    {
        // Power plan: probability-weighted by workload
        WindowsPowerPlanKind plan = workload switch
        {
            WorkloadType.Gaming or WorkloadType.HeavyCompute =>
                Random.Shared.Next(3) < 2
                    ? WindowsPowerPlanKind.HighPerformance
                    : WindowsPowerPlanKind.Balanced,
            WorkloadType.Idle =>
                WindowsPowerPlanKind.PowerSaver,
            WorkloadType.Browsing or WorkloadType.Office =>
                Random.Shared.Next(2) == 0
                    ? WindowsPowerPlanKind.PowerSaver
                    : WindowsPowerPlanKind.Balanced,
            _ => WindowsPowerPlanKind.Balanced
        };

        // GPU voltage: random step -10 to -60 mV from baseline
        int? vTarget = null;
        if (baseMv > 800)
        {
            int[] steps = { 10, 20, 30, 40, 50, 60 };
            int step = steps[Random.Shared.Next(steps.Length)];
            vTarget = Math.Max(700, baseMv - step);
        }

        // CPU max frequency: 75–100% in 5% steps
        int[] cpuMaxOpts = { 75, 80, 85, 90, 95, 100 };
        int cpuMax = cpuMaxOpts[Random.Shared.Next(cpuMaxOpts.Length)];

        // CPU min frequency: 0–20% (lower options for power-saving workloads)
        int[] cpuMinOpts = workload == WorkloadType.Idle
            ? new[] { 0, 5 }
            : new[] { 5, 10, 15, 20 };
        int cpuMin = cpuMinOpts[Random.Shared.Next(cpuMinOpts.Length)];

        // GPU power limit: 85–100%
        int[] pwrOpts = { 85, 90, 95, 100 };
        int pwrLimit = pwrOpts[Random.Shared.Next(pwrOpts.Length)];

        return new SearchCandidate
        {
            CandidateId = $"rand-{index}",
            Index = index,
            WorkloadType = workload,
            PreferredPowerPlan = plan,
            GpuVoltageMv = vTarget,
            GpuSafetyMarginMv = 10,
            GpuPowerLimitPercent = pwrLimit,
            CpuMinFrequencyPercent = cpuMin,
            CpuMaxFrequencyPercent = cpuMax,
            Weight = weight,
            Reason = $"Random candidate {index}: plan={plan}, vDelta=-{(baseMv > 0 ? baseMv - (vTarget ?? baseMv) : 0)} mV, cpuMax={cpuMax}%, cpuMin={cpuMin}%."
        };
    }

    private static WindowsPowerPlanKind GetDefaultPlanForWorkload(WorkloadType workload) => workload switch
    {
        WorkloadType.Gaming or WorkloadType.HeavyCompute => WindowsPowerPlanKind.HighPerformance,
        WorkloadType.Idle => WindowsPowerPlanKind.PowerSaver,
        _ => WindowsPowerPlanKind.Balanced
    };

    // ─────────────────────────────────────────────────────────────────────────
    //  State sync helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void SyncCurrentCandidateFields(SearchCandidate? candidate)
    {
        if (candidate is null)
        {
            State.CurrentPowerPlan = State.BaselinePowerPlan;
            State.CurrentGpuVoltageMv = State.BaselineGpuVoltageMv;
            State.CurrentGpuCoreClockMHz = State.BaselineGpuCoreClockMHz;
            State.CurrentGpuPowerLimitPercent = State.BaselineGpuPowerLimitPercent;
            return;
        }

        State.CurrentPowerPlan = candidate.PreferredPowerPlan;
        State.CurrentGpuVoltageMv = candidate.GpuVoltageMv;
        State.CurrentGpuCoreClockMHz = candidate.GpuMaxClockMHz;
        State.CurrentGpuPowerLimitPercent = candidate.GpuPowerLimitPercent;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Metric helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static double ComputeAveragePower(IReadOnlyList<SensorSnapshot> samples)
    {
        if (samples.Count == 0) return 0;
        double sum = 0;
        foreach (SensorSnapshot sample in samples)
        {
            if (sample.SystemPowerW.HasValue && sample.SystemPowerW.Value > 0)
            {
                sum += sample.SystemPowerW.Value;
                continue;
            }
            double cpu = sample.Cpu.PowerW ?? 0;
            double gpu = sample.Gpu.PowerW;
            sum += cpu + gpu;
        }
        return sum / samples.Count;
    }

    private static double ComputeAverageFan(IReadOnlyList<SensorSnapshot> samples)
    {
        if (samples.Count == 0) return 0;
        double sum = 0;
        foreach (SensorSnapshot sample in samples) sum += sample.Gpu.FanRpm;
        return sum / samples.Count;
    }

    private static double ComputeAverageGpuTemp(IReadOnlyList<SensorSnapshot> samples)
    {
        if (samples.Count == 0) return 0;
        double sum = 0;
        foreach (SensorSnapshot sample in samples) sum += sample.Gpu.TemperatureC;
        return sum / samples.Count;
    }

    private static double ComputePerformanceProxy(IReadOnlyList<SensorSnapshot> samples)
    {
        if (samples.Count == 0) return 0;
        double sum = 0;
        foreach (SensorSnapshot sample in samples) sum += sample.Cpu.UsagePercent + sample.Gpu.UsagePercent;
        return sum / samples.Count;
    }

    private static double ComputePerformanceVolatility(IReadOnlyList<SensorSnapshot> samples)
    {
        if (samples.Count < 2) return 0;
        double min = double.MaxValue;
        double max = double.MinValue;
        foreach (SensorSnapshot sample in samples)
        {
            double v = sample.Gpu.UsagePercent;
            if (v < min) min = v;
            if (v > max) max = v;
        }
        return max - min;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Mapping helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static WorkloadClass MapToWorkloadClass(WorkloadType workloadType)
    {
        return workloadType switch
        {
            WorkloadType.Idle => WorkloadClass.Idle,
            WorkloadType.Gaming => WorkloadClass.GpuHeavy,
            WorkloadType.HeavyCompute => WorkloadClass.CpuHeavy,
            _ => WorkloadClass.Mixed
        };
    }
}
