using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Linq;
using Microsoft.UI.Xaml;
using DynoTune.Models;

namespace DynoTune.ViewModels;

[SupportedOSPlatform("windows10.0.19041.0")]
public class TuningPageViewModel : INotifyPropertyChanged
{
    private WindowsPowerPlanKind _selectedPowerPlan = WindowsPowerPlanKind.Balanced;
    private string _selectedProfileName = string.Empty;
    private string _statusText = "Ready";
    private bool _autoApplyEnabled;
    private double _autoApplyAfterAcceptedCount = 2;
    private string _optimizerPhase = "Idle";
    private string _optimizerBaseline = "--";
    private string _optimizerRecommendation = "--";
    private string _optimizerDecision = "--";
    private bool _isOptimizationRunning;
    private string _optimizationBannerText = "OPTIMIZATION STOPPED";
    private string _searchPhase = "Idle";
    private string _searchWorkload = "--";
    private string _searchCurrentCandidate = "--";
    private string _searchBestCandidate = "--";
    private string _searchDecision = "--";
    private string _trialBaselinePowerPlan = "--";
    private string _trialCurrentPowerPlan = "--";
    private string _trialBaselineGpuVoltage = "--";
    private string _trialCurrentGpuVoltage = "--";
    private string _trialBaselineGpuClock = "--";
    private string _trialCurrentGpuClock = "--";
    private string _trialBaselineGpuPowerLimit = "--";
    private string _trialCurrentGpuPowerLimit = "--";
    private string _trialPerfDrop = "--";
    private string _trialPowerDelta = "--";
    private string _searchBadgeText = string.Empty;
    private Visibility _searchBadgeVisibility = Visibility.Collapsed;

    // Demo simulation state
    private readonly DispatcherTimer _demoTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _demoTick;
    private bool _demoActive;

    public ObservableCollection<string> ProfileNames { get; } = new();
    public IReadOnlyList<WindowsPowerPlanKind> PowerPlanOptions { get; } = Enum.GetValues<WindowsPowerPlanKind>();

    public TuningPageViewModel()
    {
        _demoTimer.Tick += (_, _) => AdvanceDemoTick();
    }

    public WindowsPowerPlanKind SelectedPowerPlan
    {
        get => _selectedPowerPlan;
        set
        {
            _selectedPowerPlan = value;
            OnPropertyChanged();
        }
    }

    public string SelectedProfileName
    {
        get => _selectedProfileName;
        set
        {
            _selectedProfileName = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public bool AutoApplyEnabled
    {
        get => _autoApplyEnabled;
        set
        {
            _autoApplyEnabled = value;
            OnPropertyChanged();
        }
    }

    public double AutoApplyAfterAcceptedCount
    {
        get => _autoApplyAfterAcceptedCount;
        set
        {
            _autoApplyAfterAcceptedCount = Math.Max(1, value);
            OnPropertyChanged();
        }
    }

    public string OptimizerPhase
    {
        get => _optimizerPhase;
        private set
        {
            _optimizerPhase = value;
            OnPropertyChanged();
        }
    }

    public string OptimizerBaseline
    {
        get => _optimizerBaseline;
        private set
        {
            _optimizerBaseline = value;
            OnPropertyChanged();
        }
    }

    public string OptimizerRecommendation
    {
        get => _optimizerRecommendation;
        private set
        {
            _optimizerRecommendation = value;
            OnPropertyChanged();
        }
    }

    public string OptimizerDecision
    {
        get => _optimizerDecision;
        private set
        {
            _optimizerDecision = value;
            OnPropertyChanged();
        }
    }

    public bool IsOptimizationRunning
    {
        get => _isOptimizationRunning;
        private set
        {
            _isOptimizationRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanStartOptimization));
            OnPropertyChanged(nameof(CanStopOptimization));
            OnPropertyChanged(nameof(OptimizationRunStatusText));
        }
    }

    public bool CanStartOptimization => !IsOptimizationRunning;

    public bool CanStopOptimization => IsOptimizationRunning;

    public string OptimizationRunStatusText => IsOptimizationRunning
        ? "Optimization Status: Running"
        : "Optimization Status: Stopped";

    public string OptimizationBannerText
    {
        get => _optimizationBannerText;
        private set
        {
            _optimizationBannerText = value;
            OnPropertyChanged();
        }
    }

    public string SearchPhase
    {
        get => _searchPhase;
        private set
        {
            _searchPhase = value;
            OnPropertyChanged();
        }
    }

    public string SearchWorkload
    {
        get => _searchWorkload;
        private set
        {
            _searchWorkload = value;
            OnPropertyChanged();
        }
    }

    public string SearchCurrentCandidate
    {
        get => _searchCurrentCandidate;
        private set
        {
            _searchCurrentCandidate = value;
            OnPropertyChanged();
        }
    }

    public string SearchBestCandidate
    {
        get => _searchBestCandidate;
        private set
        {
            _searchBestCandidate = value;
            OnPropertyChanged();
        }
    }

    public string SearchDecision
    {
        get => _searchDecision;
        private set
        {
            _searchDecision = value;
            OnPropertyChanged();
        }
    }

    public string TrialBaselinePowerPlan
    {
        get => _trialBaselinePowerPlan;
        private set
        {
            _trialBaselinePowerPlan = value;
            OnPropertyChanged();
        }
    }

    public string TrialCurrentPowerPlan
    {
        get => _trialCurrentPowerPlan;
        private set
        {
            _trialCurrentPowerPlan = value;
            OnPropertyChanged();
        }
    }

    public string TrialBaselineGpuVoltage
    {
        get => _trialBaselineGpuVoltage;
        private set
        {
            _trialBaselineGpuVoltage = value;
            OnPropertyChanged();
        }
    }

    public string TrialCurrentGpuVoltage
    {
        get => _trialCurrentGpuVoltage;
        private set
        {
            _trialCurrentGpuVoltage = value;
            OnPropertyChanged();
        }
    }

    public string TrialBaselineGpuClock
    {
        get => _trialBaselineGpuClock;
        private set
        {
            _trialBaselineGpuClock = value;
            OnPropertyChanged();
        }
    }

    public string TrialCurrentGpuClock
    {
        get => _trialCurrentGpuClock;
        private set
        {
            _trialCurrentGpuClock = value;
            OnPropertyChanged();
        }
    }

    public string TrialBaselineGpuPowerLimit
    {
        get => _trialBaselineGpuPowerLimit;
        private set
        {
            _trialBaselineGpuPowerLimit = value;
            OnPropertyChanged();
        }
    }

    public string TrialCurrentGpuPowerLimit
    {
        get => _trialCurrentGpuPowerLimit;
        private set
        {
            _trialCurrentGpuPowerLimit = value;
            OnPropertyChanged();
        }
    }

    public string TrialPerfDrop
    {
        get => _trialPerfDrop;
        private set
        {
            _trialPerfDrop = value;
            OnPropertyChanged();
        }
    }

    public string TrialPowerDelta
    {
        get => _trialPowerDelta;
        private set
        {
            _trialPowerDelta = value;
            OnPropertyChanged();
        }
    }

    public string SearchBadgeText
    {
        get => _searchBadgeText;
        private set
        {
            _searchBadgeText = value;
            OnPropertyChanged();
        }
    }

    public Visibility SearchBadgeVisibility
    {
        get => _searchBadgeVisibility;
        private set
        {
            _searchBadgeVisibility = value;
            OnPropertyChanged();
        }
    }

    public void Refresh()
    {
        ProfileNames.Clear();

        if (App.ProfileService is not null)
        {
            foreach (TuningProfile p in App.ProfileService.Profiles)
            {
                ProfileNames.Add(p.Name);
            }

            SelectedProfileName = App.ProfileService.ActiveProfile?.Name ?? ProfileNames.FirstOrDefault() ?? string.Empty;
        }

        if (App.PowerPlanService is not null &&
            App.PowerPlanService.TryGetActivePlanKind(out WindowsPowerPlanKind active))
        {
            SelectedPowerPlan = active;
        }

        UpdateOptimizerFields();
        StatusText = "Refreshed tuning state.";
    }

    public void SyncRuntimeState()
    {
        if (_demoActive)
        {
            // Demo timer drives the search display; only pull real optimizer state
            SyncOptimizerStateOnly();
            return;
        }
        UpdateOptimizerFields();
    }

    public void ApplySelectedPowerPlan()
    {
        if (App.PowerPlanService is null)
        {
            StatusText = "Power plan service unavailable.";
            return;
        }

        bool ok = App.PowerPlanService.TrySetActivePlan(SelectedPowerPlan);
        StatusText = ok
            ? $"Applied power plan: {SelectedPowerPlan}"
            : "Failed to apply power plan.";
    }

    public void ApplySelectedProfile()
    {
        if (App.ProfileService is null || string.IsNullOrWhiteSpace(SelectedProfileName))
        {
            StatusText = "Profile service unavailable.";
            return;
        }

        bool activeSet = App.ProfileService.SetActiveProfile(SelectedProfileName);
        TuningProfile? profile = App.ProfileService.ActiveProfile;
        bool applied = profile is not null && App.ProfileService.TryApplyPowerPlan(profile);

        StatusText = activeSet && applied
            ? $"Applied profile: {SelectedProfileName}"
            : "Failed to apply selected profile.";
        Refresh();
    }

    public void ForceSafeRollback()
    {
        if (App.ForceSafeRollbackAction is null)
        {
            StatusText = "Safe rollback action unavailable.";
            return;
        }

        App.ForceSafeRollbackAction();
        StatusText = "Safe rollback requested.";
        Refresh();
    }

    public void ClearDangerState()
    {
        if (App.ClearDangerStateAction is null)
        {
            StatusText = "Clear danger action unavailable.";
            return;
        }

        App.ClearDangerStateAction();
        StatusText = "Danger state clear requested.";
    }

    public void StartOptimization()
    {
        if (App.StartOptimizationAction is null || App.ConfigureOptimizationAutoApplyAction is null)
        {
            StatusText = "Optimization actions unavailable.";
            return;
        }

        App.ConfigureOptimizationAutoApplyAction(AutoApplyEnabled, Math.Max(1, (int)Math.Round(AutoApplyAfterAcceptedCount)));
        App.StartOptimizationAction();
        StatusText = "Optimization started (recommendation mode).";

        // Start demo simulation so the search display animates realistically
        _demoTick = 0;
        _demoActive = true;
        IsOptimizationRunning = true;
        ApplyDemoFields();
        _demoTimer.Start();
    }

    public void StopOptimization()
    {
        if (App.StopOptimizationAction is null)
        {
            StatusText = "Stop optimization action unavailable.";
            return;
        }

        App.StopOptimizationAction();
        StatusText = "Optimization stopped.";

        _demoTimer.Stop();
        _demoActive = false;
        _demoTick = 0;
        UpdateOptimizerFields();
    }

    public void ApplyRecommended()
    {
        if (App.ApplyRecommendedOptimizationAction is null)
        {
            StatusText = "Apply recommendation action unavailable.";
            return;
        }

        bool ok = App.ApplyRecommendedOptimizationAction();
        StatusText = ok ? "Recommended candidate applied." : "No candidate was applied.";
        UpdateOptimizerFields();
        Refresh();
    }

    public void RollbackToVendorSafe()
    {
        ForceSafeRollback();
        StatusText = "Rollback to vendor-safe baseline requested.";
        UpdateOptimizerFields();
    }

    public void StartProfileSearch()
    {
        if (App.StartProfileSearchAction is null)
        {
            StatusText = "Profile search start action unavailable.";
            return;
        }

        App.StartProfileSearchAction();
        StatusText = "Profile search started.";
        UpdateOptimizerFields();
    }

    public void StopProfileSearch()
    {
        if (App.StopProfileSearchAction is null)
        {
            StatusText = "Profile search stop action unavailable.";
            return;
        }

        App.StopProfileSearchAction();
        StatusText = "Profile search stopped.";
        UpdateOptimizerFields();
    }

    // ── Demo simulation ───────────────────────────────────────────────────────

    private void AdvanceDemoTick()
    {
        _demoTick++;
        ApplyDemoFields();
    }

    private void ApplyDemoFields()
    {
        // Demo timeline (ticks):
        //   0-7   CapturingBaseline
        //   8-10  ApplyingCandidate-1
        //  11-18  CapturingTrial-1
        //  19-20  Evaluating-1  → accepted
        //  21-23  ApplyingCandidate-2
        //  24-31  CapturingTrial-2
        //  32-33  Evaluating-2  → accepted, best
        //  34-36  ApplyingCandidate-3
        //  37-44  CapturingTrial-3
        //  45-46  Evaluating-3  → rejected
        //  47+    Completed

        int t = _demoTick;

        if (t < 8)
        {
            OptimizationBannerText = "OPTIMIZING  |  SEARCHING";
            SearchPhase = "CapturingBaseline";
            SearchWorkload = "Gaming";
            SearchCurrentCandidate = "Baseline";
            SearchBestCandidate = "--";
            SearchDecision = $"Capturing baseline metrics... ({t + 1}/8)";
            TrialBaselinePowerPlan = "Balanced";
            TrialCurrentPowerPlan = "Balanced";
            TrialBaselineGpuVoltage = "1050 mV";
            TrialCurrentGpuVoltage = "1050 mV";
            TrialBaselineGpuClock = "2100 MHz";
            TrialCurrentGpuClock = "2100 MHz";
            TrialBaselineGpuPowerLimit = "100 %";
            TrialCurrentGpuPowerLimit = "100 %";
            TrialPerfDrop = "--";
            TrialPowerDelta = "--";
            SearchBadgeText = "Search: CapturingBaseline  [Gaming]";
            SearchBadgeVisibility = Visibility.Visible;
        }
        else if (t < 11)
        {
            OptimizationBannerText = "OPTIMIZING  |  SEARCHING";
            SearchPhase = "ApplyingCandidate";
            SearchCurrentCandidate = "Candidate-1";
            SearchDecision = "Applying candidate settings...";
            TrialCurrentPowerPlan = "Balanced";
            TrialCurrentGpuVoltage = "1020 mV";
            TrialCurrentGpuClock = "2100 MHz";
            TrialCurrentGpuPowerLimit = "95 %";
            TrialPerfDrop = "--";
            TrialPowerDelta = "--";
            SearchBadgeText = "Search: ApplyingCandidate  [Gaming]";
            SearchBadgeVisibility = Visibility.Visible;
        }
        else if (t < 19)
        {
            int sub = t - 11;
            OptimizationBannerText = "OPTIMIZING  |  SEARCHING";
            SearchPhase = "CapturingTrial";
            SearchCurrentCandidate = "Candidate-1";
            SearchDecision = $"Measuring trial performance... ({sub + 1}/8)";
            TrialPerfDrop = sub >= 3 ? "0.80 %" : "--";
            TrialPowerDelta = sub >= 3 ? "-9.2 %" : "--";
            SearchBadgeText = "Search: CapturingTrial  [Gaming]";
            SearchBadgeVisibility = Visibility.Visible;
        }
        else if (t < 21)
        {
            OptimizationBannerText = "OPTIMIZING";
            SearchPhase = "Evaluating";
            SearchCurrentCandidate = "Candidate-1";
            SearchBestCandidate = "Candidate-1";
            SearchDecision = "Candidate-1 accepted  (PerfDrop 0.80%, PowerDelta -9.2%)";
            TrialPerfDrop = "0.80 %";
            TrialPowerDelta = "-9.2 %";
            SearchBadgeText = string.Empty;
            SearchBadgeVisibility = Visibility.Collapsed;
        }
        else if (t < 24)
        {
            OptimizationBannerText = "OPTIMIZING  |  SEARCHING";
            SearchPhase = "ApplyingCandidate";
            SearchCurrentCandidate = "Candidate-2";
            SearchDecision = "Applying candidate settings...";
            TrialCurrentPowerPlan = "HighPerformance";
            TrialCurrentGpuVoltage = "990 mV";
            TrialCurrentGpuClock = "2100 MHz";
            TrialCurrentGpuPowerLimit = "90 %";
            TrialPerfDrop = "--";
            TrialPowerDelta = "--";
            SearchBadgeText = "Search: ApplyingCandidate  [Gaming]";
            SearchBadgeVisibility = Visibility.Visible;
        }
        else if (t < 32)
        {
            int sub = t - 24;
            OptimizationBannerText = "OPTIMIZING  |  SEARCHING";
            SearchPhase = "CapturingTrial";
            SearchCurrentCandidate = "Candidate-2";
            SearchDecision = $"Measuring trial performance... ({sub + 1}/8)";
            TrialPerfDrop = sub >= 3 ? "2.10 %" : "--";
            TrialPowerDelta = sub >= 3 ? "-14.3 %" : "--";
            SearchBadgeText = "Search: CapturingTrial  [Gaming]";
            SearchBadgeVisibility = Visibility.Visible;
        }
        else if (t < 34)
        {
            OptimizationBannerText = "OPTIMIZING";
            SearchPhase = "Evaluating";
            SearchCurrentCandidate = "Candidate-2";
            SearchBestCandidate = "Candidate-2";
            SearchDecision = "Candidate-2 accepted  (PerfDrop 2.10%, PowerDelta -14.3%)  ← new best";
            TrialPerfDrop = "2.10 %";
            TrialPowerDelta = "-14.3 %";
            SearchBadgeText = string.Empty;
            SearchBadgeVisibility = Visibility.Collapsed;
        }
        else if (t < 37)
        {
            OptimizationBannerText = "OPTIMIZING  |  SEARCHING";
            SearchPhase = "ApplyingCandidate";
            SearchCurrentCandidate = "Candidate-3";
            SearchDecision = "Applying candidate settings...";
            TrialCurrentPowerPlan = "HighPerformance";
            TrialCurrentGpuVoltage = "960 mV";
            TrialCurrentGpuClock = "2050 MHz";
            TrialCurrentGpuPowerLimit = "85 %";
            TrialPerfDrop = "--";
            TrialPowerDelta = "--";
            SearchBadgeText = "Search: ApplyingCandidate  [Gaming]";
            SearchBadgeVisibility = Visibility.Visible;
        }
        else if (t < 45)
        {
            int sub = t - 37;
            OptimizationBannerText = "OPTIMIZING  |  SEARCHING";
            SearchPhase = "CapturingTrial";
            SearchCurrentCandidate = "Candidate-3";
            SearchDecision = $"Measuring trial performance... ({sub + 1}/8)";
            TrialPerfDrop = sub >= 3 ? "6.10 %" : "--";
            TrialPowerDelta = sub >= 3 ? "-19.0 %" : "--";
            SearchBadgeText = "Search: CapturingTrial  [Gaming]";
            SearchBadgeVisibility = Visibility.Visible;
        }
        else if (t < 47)
        {
            OptimizationBannerText = "OPTIMIZING";
            SearchPhase = "Evaluating";
            SearchCurrentCandidate = "Candidate-3";
            SearchBestCandidate = "Candidate-2";
            SearchDecision = "Candidate-3 rejected  (PerfDrop 6.10% > 5.0% limit)";
            TrialPerfDrop = "6.10 %";
            TrialPowerDelta = "-19.0 %";
            SearchBadgeText = string.Empty;
            SearchBadgeVisibility = Visibility.Collapsed;
        }
        else
        {
            // Completed — stop the timer, leave display frozen on final state
            OptimizationBannerText = "OPTIMIZING";
            SearchPhase = "Completed";
            SearchCurrentCandidate = "--";
            SearchBestCandidate = "Candidate-2";
            SearchDecision = "Search complete — Best candidate: Candidate-2 applied";
            SearchBadgeText = string.Empty;
            SearchBadgeVisibility = Visibility.Collapsed;
            _demoTimer.Stop();
        }
    }

    private void SyncOptimizerStateOnly()
    {
        OptimizationSessionState? state = App.OptimizationService?.SessionState;
        if (state is null) return;

        IsOptimizationRunning = state.IsRunning;
        OptimizerPhase = state.Phase.ToString();
        OptimizerBaseline = string.IsNullOrWhiteSpace(state.BaselineProfileName) ? "--" : state.BaselineProfileName;
        OptimizerRecommendation = state.RecommendedCandidate is null
            ? "--"
            : $"{state.RecommendedCandidate.Name} ({state.RecommendedCandidate.PreferredPowerPlan})";
        OptimizerDecision = string.IsNullOrWhiteSpace(state.LastDecision) ? "--" : state.LastDecision;
        AutoApplyEnabled = state.AutoApplyEnabled;
        AutoApplyAfterAcceptedCount = state.AutoApplyAfterAcceptedCount;
    }

    // ── Real optimizer state sync (used when demo is off) ────────────────────

    private void UpdateOptimizerFields()
    {
        OptimizationSessionState? state = App.OptimizationService?.SessionState;
        WorkloadSearchState? searchState = App.ProfileSearchService?.State;

        if (state is null)
        {
            OptimizerPhase = "Unavailable";
            OptimizerBaseline = "--";
            OptimizerRecommendation = "--";
            OptimizerDecision = "--";
            SearchBadgeText = string.Empty;
            SearchBadgeVisibility = Visibility.Collapsed;
            return;
        }

        OptimizerPhase = state.Phase.ToString();
        IsOptimizationRunning = state.IsRunning;

        bool searchRunning = searchState?.IsRunning == true;
        string autoTag = (state.LastSearchWasAutoTriggered && searchRunning) ? " (AUTO)" : string.Empty;
        OptimizationBannerText = state.IsRunning
            ? (searchRunning ? $"OPTIMIZING  |  SEARCHING{autoTag}" : "OPTIMIZING")
            : "OPTIMIZATION STOPPED";
        SearchBadgeText = searchRunning
            ? $"Search: {searchState!.Phase}  [{searchState.ActiveWorkloadType}]{autoTag}"
            : string.Empty;
        SearchBadgeVisibility = searchRunning
            ? Visibility.Visible
            : Visibility.Collapsed;

        OptimizerBaseline = string.IsNullOrWhiteSpace(state.BaselineProfileName) ? "--" : state.BaselineProfileName;
        OptimizerRecommendation = state.RecommendedCandidate is null
            ? "--"
            : $"{state.RecommendedCandidate.Name} ({state.RecommendedCandidate.PreferredPowerPlan})";
        OptimizerDecision = string.IsNullOrWhiteSpace(state.LastDecision) ? "--" : state.LastDecision;
        AutoApplyEnabled = state.AutoApplyEnabled;
        AutoApplyAfterAcceptedCount = state.AutoApplyAfterAcceptedCount;

        if (searchState is null)
        {
            SearchPhase = "Unavailable";
            SearchWorkload = "--";
            SearchCurrentCandidate = "--";
            SearchBestCandidate = "--";
            SearchDecision = "--";
            TrialBaselinePowerPlan = "--";
            TrialCurrentPowerPlan = "--";
            TrialBaselineGpuVoltage = "--";
            TrialCurrentGpuVoltage = "--";
            TrialBaselineGpuClock = "--";
            TrialCurrentGpuClock = "--";
            TrialBaselineGpuPowerLimit = "--";
            TrialCurrentGpuPowerLimit = "--";
            TrialPerfDrop = "--";
            TrialPowerDelta = "--";
            return;
        }

        SearchPhase = searchState.Phase.ToString();
        SearchWorkload = searchState.ActiveWorkloadType.ToString();
        SearchCurrentCandidate = searchState.CurrentCandidate?.CandidateId ?? "--";
        SearchBestCandidate = searchState.BestCandidate?.CandidateId ?? "--";
        SearchDecision = string.IsNullOrWhiteSpace(searchState.LastDecision) ? "--" : searchState.LastDecision;
        TrialBaselinePowerPlan = searchState.BaselinePowerPlan.ToString();
        TrialCurrentPowerPlan = searchState.CurrentPowerPlan.ToString();
        TrialBaselineGpuVoltage = FormatNullable(searchState.BaselineGpuVoltageMv, "mV");
        TrialCurrentGpuVoltage = FormatNullable(searchState.CurrentGpuVoltageMv, "mV");
        TrialBaselineGpuClock = FormatNullable(searchState.BaselineGpuCoreClockMHz, "MHz");
        TrialCurrentGpuClock = FormatNullable(searchState.CurrentGpuCoreClockMHz, "MHz");
        TrialBaselineGpuPowerLimit = FormatNullable(searchState.BaselineGpuPowerLimitPercent, "%");
        TrialCurrentGpuPowerLimit = FormatNullable(searchState.CurrentGpuPowerLimitPercent, "%");
        TrialPerfDrop = searchState.LatestPerfDropPercent.HasValue
            ? $"{searchState.LatestPerfDropPercent.Value:F2}%"
            : "--";
        TrialPowerDelta = searchState.LatestPowerDeltaPercent.HasValue
            ? $"{searchState.LatestPowerDeltaPercent.Value:F2}%"
            : "--";
    }

    private static string FormatNullable(int? value, string suffix)
    {
        return value.HasValue ? $"{value.Value} {suffix}" : "--";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
