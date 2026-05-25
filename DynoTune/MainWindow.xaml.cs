using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using DynoTune.Models;
using DynoTune.Services;
using DynoTune.ViewModels;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DynoTune;

[SupportedOSPlatform("windows10.0.19041.0")]
public sealed partial class MainWindow : Window
{
    // ── Hardware services ─────────────────────────────────────────────────────
    private readonly AppSettingsService _settingsService = new();
    private readonly AmdAdlxService _gpuService = new();
    private readonly LibreHardwareMonitorService _cpuService = new();
    private readonly MonitoringService _monitoringService;
    private readonly LoggingService _loggingService;
    private readonly WorkloadClassifier _workloadClassifier = new();
    private readonly WindowsPowerPlanService _powerPlanService = new();
    private readonly ProfileService _profileService;
    private readonly AdaptiveOptimizationService _optimizationService;
    private readonly ProfileSearchService _profileSearchService;
    private readonly StabilityMonitorService _stabilityMonitor = new();
    private readonly TelemetryRepository _telemetryRepo = new();

    // ── Session state ─────────────────────────────────────────────────────────
    private readonly DateTime _sessionStartUtc = DateTime.UtcNow;
    private readonly DispatcherTimer _loggingTimer = new();
    private readonly DangerState _dangerState = new();
    private readonly List<string> _dangerAudit = new();
    private readonly string _runStateFilePath;
    private int _tickCount;
    private bool _isShuttingDown;
    private bool _previousRunUnclean;

    // Stability is checked every 60 ticks to avoid blocking the UI thread frequently.
    private const int StabilityCheckIntervalTicks = 60;
    private const int DangerCheckIntervalTicks = 10;
    private const int CpuPowerSettingsCheckIntervalTicks = 10;
    private static readonly TimeSpan RollingDangerWindow = TimeSpan.FromMinutes(2);

    private string? _lastClassifierLogKey;

    public MainWindow()
    {
        InitializeComponent();

        _settingsService.Load();
        _monitoringService = new MonitoringService(_cpuService, _gpuService);
        _loggingService = new LoggingService(_monitoringService);
        _profileService = new ProfileService(_powerPlanService);
        _optimizationService = new AdaptiveOptimizationService(_profileService, _powerPlanService);
        _profileSearchService = new ProfileSearchService(_profileService, _powerPlanService, _gpuService, _settingsService);
        App.ConfigureRuntimeServices(
            _settingsService,
            _loggingService,
            _profileService,
            _powerPlanService,
            _optimizationService,
            _profileSearchService,
            ForceSafeRollbackNow,
            ClearDangerStateNow,
            StartOptimizationNow,
            StopOptimizationNow,
            ConfigureOptimizationAutoApply,
            ApplyRecommendedOptimizationNow,
            StartProfileSearchNow,
            StopProfileSearchNow,
            ApplySettingsNow,
            _telemetryRepo);
        _runStateFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DynoTune",
            "state",
            "last-run.json");

        // Resize to a comfortable default.
        AppWindow.Resize(new SizeInt32(1320, 820));

        InitializeHardware();
        NavigateToMonitoring();
        Closed += MainWindow_Closed;
    }

    private static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void InitializeHardware()
    {
        bool isAdmin = IsRunningAsAdmin();
        Debug.WriteLine($"Running as admin: {isAdmin}");
        if (!isAdmin)
        {
            Debug.WriteLine("WARNING: Not running as administrator. CPU temp/clock/power sensors may be unavailable.");
            Debug.WriteLine(
                "F5 / default builds use asInvoker. For full LHM sensors: build with -p:UseAdminApplicationManifest=true, install the MSIX, launch from Start and approve UAC (F5 with admin manifest fails VS activation).");
        }

        bool gpuInitialized = _gpuService.Initialize();
        Debug.WriteLine($"ADLX init: {gpuInitialized}");
        _gpuService.DumpSensorInventoryOnce();

        _cpuService.Initialize();
        _cpuService.DumpSensorInventoryOnce();

        if (_powerPlanService.TryGetActivePlanKind(out WindowsPowerPlanKind planKind))
        {
            Debug.WriteLine($"Active Windows power plan: {planKind}");
            _profileService.CaptureVendorSafeBaseline(planKind, _monitoringService.GetCurrentSnapshot().Gpu);
        }

        LoadRunStateAndMarkStarted();
        if (_previousRunUnclean)
        {
            TriggerDanger(
                DangerReason.AppCrashDetected,
                "Previous run ended uncleanly (possible app crash).",
                DateTime.UtcNow,
                DangerLevel.Warning);
        }

        _loggingTimer.Interval = TimeSpan.FromSeconds(1);
        _loggingTimer.Tick += LoggingTimer_Tick;
        _loggingTimer.Start();

        _optimizationService.StartSession();
        Debug.WriteLine("[Optimizer] Auto-started on launch.");
    }

    private void NavigateToMonitoring()
    {
        ContentFrame.Navigate(typeof(Views.MonitoringPage));

        // Select the Monitor nav item so it appears highlighted.
        foreach (object item in NavView.MenuItems)
        {
            if (item is NavigationViewItem nvi && nvi.Tag as string == "monitoring")
            {
                NavView.SelectedItem = nvi;
                break;
            }
        }
    }

    private void NavView_SelectionChanged(NavigationView _sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is NavigationViewItem nvi && nvi.Tag is string tag)
        {
            _ = tag switch
            {
                "monitoring" => ContentFrame.Navigate(typeof(Views.MonitoringPage)),
                "logs"       => ContentFrame.Navigate(typeof(Views.LogsPage)),
                "profiles"   => ContentFrame.Navigate(typeof(Views.ProfilesPage)),
                "tuning"     => ContentFrame.Navigate(typeof(Views.TuningPage)),
                "demo"       => ContentFrame.Navigate(typeof(Views.DemoPage)),
                "settings"   => ContentFrame.Navigate(typeof(Views.SettingsPage)),
                _            => false
            };
        }
    }

    // ── Telemetry tick ────────────────────────────────────────────────────────

    private void LoggingTimer_Tick(object? sender, object e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        try
        {
            _tickCount++;
#if DEBUG
            ProcessDebugDangerSimulation();
#endif

            SensorSnapshot snapshot = _monitoringService.GetCurrentSnapshot();
            ClassificationResult classification = _workloadClassifier.Classify(snapshot);
            string powerPlanLabel = GetPowerPlanLabel();
            _optimizationService.Tick(snapshot, classification, _dangerState,
                _profileSearchService.State.IsRunning);
            _profileSearchService.Tick(snapshot, classification, _dangerState);

            // Auto-trigger profile search when optimizer signals the current workload needs one.
            if (_optimizationService.SessionState.ShouldTriggerSearch
                && !_profileSearchService.State.IsRunning
                && _dangerState.Level == DangerLevel.Safe)
            {
                _optimizationService.SessionState.LastSearchWasAutoTriggered = true;
                _optimizationService.NotifySearchTriggered();
                _profileSearchService.Start(SearchObjective.LowestPowerWithPerfFloor);
                Debug.WriteLine($"[AutoSearch] Triggered for {_optimizationService.SessionState.ActiveWorkloadType}.");
            }

            // Log to CSV record.
            string activeProfileName = _profileService.ActiveProfile?.Name ?? "Stock";
            LogRecord record = _loggingService.CreateRecordFromSnapshot(snapshot, activeProfileName, classification);
            record.DangerLevel = _dangerState.Level;
            record.DangerReason = _dangerState.Reason;
            record.DangerReasonDetail = _dangerState.ReasonDetail;
            record.DangerRollbackApplied = _dangerState.AutoRollbackApplied;
            record.OptimizerPhase = _optimizationService.SessionState.Phase.ToString();
            record.OptimizerCandidateName = _optimizationService.SessionState.RecommendedCandidate?.Name ?? string.Empty;
            record.OptimizerCandidateApplied = _optimizationService.SessionState.LastResult?.CandidateApplied ?? false;
            record.OptimizerAcceptanceReason = _optimizationService.SessionState.LastResult?.AcceptanceReason ?? string.Empty;
            record.OptimizerRollbackReason = _optimizationService.SessionState.LastResult?.RollbackReason ?? string.Empty;
            record.SearchSessionId = _profileSearchService.State.SessionId;
            record.SearchWorkloadType = _profileSearchService.State.ActiveWorkloadType;
            record.SearchPhase = _profileSearchService.State.Phase.ToString();
            record.SearchCandidateId = _profileSearchService.State.CurrentCandidate?.CandidateId ?? string.Empty;
            record.SearchCandidateIndex = _profileSearchService.State.CandidateIndex;
            record.SearchAccepted = _profileSearchService.State.Evaluations.LastOrDefault()?.Accepted ?? false;
            record.SearchDecision = string.IsNullOrWhiteSpace(_profileSearchService.State.LastCandidateDecision)
                ? _profileSearchService.State.LastDecision
                : _profileSearchService.State.LastCandidateDecision;
            SearchEvaluation? latestEvaluation = _profileSearchService.State.Evaluations.LastOrDefault();
            record.SearchPerfDropPercent = latestEvaluation?.PerfDropPercentVsBaseline;
            record.SearchPowerDeltaPercent = latestEvaluation?.PowerDeltaPercentVsBaseline;
            record.SearchRequestedGpuVoltageMv = _profileSearchService.State.LastRequestedGpuVoltageMv;
            record.SearchAppliedGpuVoltageMv = _profileSearchService.State.LastAppliedGpuVoltageMv;
            record.SearchGpuSafetyMarginMv = _profileSearchService.State.LastGpuSafetyMarginMv;
            record.SearchRequestedGpuClockMHz = _profileSearchService.State.LastRequestedGpuClockMHz;
            record.SearchRequestedGpuPowerLimitPercent = _profileSearchService.State.LastRequestedGpuPowerLimitPercent;
            record.SearchRequestedPowerPlan = _profileSearchService.State.LastRequestedPowerPlan?.ToString() ?? string.Empty;
            record.SearchConfirmedPowerPlan = _profileSearchService.State.LastConfirmedPowerPlan?.ToString() ?? string.Empty;
            record.SearchPowerPlanConfirmed = _profileSearchService.State.LastPowerPlanConfirmed;
            record.SearchCandidateWeight = _profileSearchService.State.CurrentCandidate?.Weight;
            record.SearchLossEnergy = latestEvaluation?.LossEnergy;
            record.SearchLossPerf = latestEvaluation?.LossPerf;
            record.SearchLossTotal = latestEvaluation?.LossTotal;
            record.SearchWeightAfterUpdate = latestEvaluation?.WeightAfterUpdate;
            record.SearchObjectiveScore = latestEvaluation?.ObjectiveScore;
            record.SearchRiskPenalty = _profileSearchService.State.LastRiskPenalty;
            record.SearchVolatility = _profileSearchService.State.LastVolatility;
            record.SearchVoltageBoundaryUpdate = latestEvaluation?.VoltageBoundaryUpdate ?? string.Empty;
            record.SearchVLastKnownGoodMv = _profileSearchService.State.VLastKnownGoodMv;
            record.SearchVFirstFailMv = _profileSearchService.State.VFirstFailMv;
            record.SearchBaselineAvgPowerW = _profileSearchService.State.BaselineAvgPowerW > 0 ? _profileSearchService.State.BaselineAvgPowerW : null;
            record.SearchBaselineAvgPerfProxy = _profileSearchService.State.BaselineAvgPerfProxy > 0 ? _profileSearchService.State.BaselineAvgPerfProxy : null;
            record.SearchBaselineAvgFanRpm = _profileSearchService.State.BaselineAvgFanRpm > 0 ? _profileSearchService.State.BaselineAvgFanRpm : null;
            record.SearchGpuApplySucceeded = _profileSearchService.State.LastGpuApplySucceeded;
            record.SearchCpuOnlyFallbackUsed = _profileSearchService.State.LastCpuOnlyFallbackUsed;
            record.SearchApplyFailureReason = _profileSearchService.State.LastApplyFailureReason;
            record.SearchCpuOnlyFallbackCount = _profileSearchService.State.CpuOnlyFallbackCount;
            record.SearchCandidateDecision = _profileSearchService.State.LastCandidateDecision;
            record.SearchNextAction = _profileSearchService.State.LastNextAction;
            _loggingService.AddRecord(record);

            // Push live data to the UI ViewModel.
            App.LiveData.Update(snapshot, classification, powerPlanLabel);
            _telemetryRepo.Insert(DateTime.UtcNow,
                snapshot.Cpu.UsagePercent,
                snapshot.Gpu.UsagePercent,
                _optimizationService.SessionState.IsRunning);

            // Refresh CPU power management settings at a lower rate (registry reads are fast but unnecessary every tick).
            if (_tickCount % CpuPowerSettingsCheckIntervalTicks == 0)
            {
                App.LiveData.UpdateCpuPowerSettings(
                    _powerPlanService.TryGetCpuMinFrequencyPercent(),
                    _powerPlanService.TryGetCpuMaxFrequencyPercent(),
                    _powerPlanService.TryGetCpuBoostMode());
            }

            // Suggest profile but don't auto-apply (safe, read-only for now).
            TuningProfile? suggested = _profileService.SuggestProfile(classification);
            string classifierKey = $"{classification.WorkloadType}|{classification.Reason}|{suggested?.Name ?? ""}";
            if (classifierKey != _lastClassifierLogKey)
            {
                _lastClassifierLogKey = classifierKey;
                Debug.WriteLine($"[Classifier] {classification.WorkloadType} ({classification.Reason})  Suggested: {suggested?.Name ?? "(none)"}");
            }

            // Periodic stability check (non-blocking – event log reads are fast for small windows).
            if (_tickCount % StabilityCheckIntervalTicks == 0)
            {
                RefreshStability();
            }
            if (_tickCount % DangerCheckIntervalTicks == 0)
            {
                EvaluateDangerRollingWindow();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Telemetry tick failed: {ex.Message}");
        }
    }

    private string GetPowerPlanLabel()
    {
        return _powerPlanService.TryGetActivePlanKind(out WindowsPowerPlanKind kind)
            ? kind.ToString()
            : "Custom";
    }

    private void RefreshStability()
    {
        try
        {
            StabilitySnapshot snap = _stabilityMonitor.GetSnapshotSince(_sessionStartUtc);
            App.LiveData.UpdateStability(snap.WheaErrorCount, snap.GpuDriverResetCount);
            App.LiveData.UpdateDanger(_dangerState);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Stability check failed: {ex.Message}");
        }
    }

    private void EvaluateDangerRollingWindow()
    {
        DateTime sinceUtc = DateTime.UtcNow - RollingDangerWindow;
        StabilitySnapshot rolling = _stabilityMonitor.GetSnapshotSince(sinceUtc);

        if (rolling.WheaErrorCount > 0)
        {
            TriggerDanger(
                DangerReason.WheaEvent,
                $"WHEA events in last {(int)RollingDangerWindow.TotalSeconds}s: {rolling.WheaErrorCount}",
                DateTime.UtcNow,
                DangerLevel.Critical);
            return;
        }

        if (rolling.GpuDriverResetCount > 0)
        {
            TriggerDanger(
                DangerReason.GpuDriverReset,
                $"GPU driver resets in last {(int)RollingDangerWindow.TotalSeconds}s: {rolling.GpuDriverResetCount}",
                DateTime.UtcNow,
                DangerLevel.Critical);
            return;
        }

        // Keep app crash warning for the session, otherwise clear danger state.
        if (!_previousRunUnclean && _dangerState.Level != DangerLevel.Safe)
        {
            _dangerState.Level = DangerLevel.Safe;
            _dangerState.Reason = DangerReason.None;
            _dangerState.ReasonDetail = string.Empty;
            _dangerState.AutoRollbackApplied = false;
            App.LiveData.UpdateDanger(_dangerState);
        }
    }

    private void TriggerDanger(DangerReason reason, string detail, DateTime triggeredAtUtc, DangerLevel level)
    {
        if (_dangerState.FirstTriggeredUtc is null)
        {
            _dangerState.FirstTriggeredUtc = triggeredAtUtc;
        }
        _dangerState.LastTriggeredUtc = triggeredAtUtc;
        _dangerState.Level = level;
        _dangerState.Reason = reason;
        _dangerState.ReasonDetail = detail;

        if (!_dangerState.AutoRollbackApplied)
        {
            TuningProfile fallback = _profileService.GetSafeFallbackProfile();
            bool applied = _profileService.TryApplyPowerPlan(fallback);
            _dangerState.AutoRollbackApplied = applied;
            _dangerAudit.Add(
                $"{triggeredAtUtc:O} | Danger={reason} | Level={level} | RollbackProfile={fallback.Name} | Applied={applied}");
            Debug.WriteLine(
                $"[Danger] {reason} :: {detail} | Auto-rollback profile '{fallback.Name}' apply result: {applied}");
        }
        else
        {
            _dangerAudit.Add($"{triggeredAtUtc:O} | Danger={reason} | Level={level} | Detail={detail}");
            Debug.WriteLine($"[Danger] {reason} :: {detail}");
        }

        App.LiveData.UpdateDanger(_dangerState);
    }

    private void ForceSafeRollbackNow()
    {
        _optimizationService.RollbackToVendorSafe("Manual safe rollback requested from tuning UI.");
        TuningProfile fallback = _profileService.GetSafeFallbackProfile();
        bool applied = _profileService.TryApplyPowerPlan(fallback);
        TriggerDanger(
            DangerReason.ManualRollback,
            $"Manual safe rollback requested. Profile={fallback.Name}, applied={applied}",
            DateTime.UtcNow,
            DangerLevel.Warning);
    }

    private void ClearDangerStateNow()
    {
        _dangerState.Level = DangerLevel.Safe;
        _dangerState.Reason = DangerReason.None;
        _dangerState.ReasonDetail = string.Empty;
        _dangerState.AutoRollbackApplied = false;
        _dangerState.FirstTriggeredUtc = null;
        _dangerState.LastTriggeredUtc = null;
        App.LiveData.UpdateDanger(_dangerState);
        Debug.WriteLine("[Danger] Manual clear requested from UI.");
    }

    private void StartOptimizationNow()
    {
        _optimizationService.StartSession();
        Debug.WriteLine("[Optimizer] Started.");
    }

    private void StopOptimizationNow()
    {
        _optimizationService.StopSession();
        Debug.WriteLine("[Optimizer] Stopped.");
    }

    private void ConfigureOptimizationAutoApply(bool enabled, int afterAcceptedCount)
    {
        _optimizationService.SessionState.AutoApplyEnabled = enabled;
        _optimizationService.SessionState.AutoApplyAfterAcceptedCount = Math.Max(1, afterAcceptedCount);
    }

    private bool ApplyRecommendedOptimizationNow()
    {
        return _optimizationService.ApplyRecommendedCandidate();
    }

    private void StartProfileSearchNow()
    {
        _optimizationService.SessionState.LastSearchWasAutoTriggered = false;
        _profileSearchService.Start(SearchObjective.LowestPowerWithPerfFloor);
        Debug.WriteLine("[ProfileSearch] Started (manual).");
    }

    private void StopProfileSearchNow()
    {
        _profileSearchService.Stop("Search stopped from tuning UI.");
        Debug.WriteLine("[ProfileSearch] Stopped.");
    }

    private void ApplySettingsNow()
    {
        TimeSpan newInterval = TimeSpan.FromMilliseconds(
            Math.Clamp(_settingsService.Current.SamplingIntervalMs, 500, 5000));
        if (_loggingTimer.Interval != newInterval)
        {
            _loggingTimer.Stop();
            _loggingTimer.Interval = newInterval;
            _loggingTimer.Start();
            Debug.WriteLine($"[Settings] Sampling interval updated to {newInterval.TotalMilliseconds} ms.");
        }
    }

    // ── Window close ─────────────────────────────────────────────────────────

    private async void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _isShuttingDown = true;
        _loggingTimer.Stop();

        try
        {
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DynoTune",
                "logs");
            Directory.CreateDirectory(logDir);

            string csvPath = Path.Combine(logDir, $"telemetry-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            await _loggingService.SaveToCsvAsync(csvPath);
            Debug.WriteLine($"CSV exported: {csvPath}");

            string stabilityPath = Path.Combine(logDir, $"stability-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            await SaveStabilitySessionLogAsync(stabilityPath);
            Debug.WriteLine($"Stability log exported: {stabilityPath}");

            string searchSummaryPath = Path.Combine(logDir, $"search-summary-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            await File.WriteAllTextAsync(searchSummaryPath, _profileSearchService.BuildSummaryReport(), Encoding.UTF8);
            Debug.WriteLine($"Search summary exported: {searchSummaryPath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Export failed: {ex.Message}");
        }

        _gpuService.Shutdown();
        _cpuService.Shutdown();
        _telemetryRepo.Dispose();
        MarkRunState(cleanShutdown: true);
    }

    private async Task SaveStabilitySessionLogAsync(string filePath)
    {
        StabilitySnapshot snapshot = _stabilityMonitor.GetSnapshotSince(_sessionStartUtc);

        var sb = new StringBuilder();
        sb.AppendLine("DynoTune stability session log");
        sb.AppendLine("Counts are from the Windows System event log (WHEA-Logger and Display 4101), not raw hardware registers.");
        sb.AppendLine("Danger state includes auto-safe rollback behavior.");
        sb.AppendLine();
        sb.Append("Window start (UTC): ").AppendLine(snapshot.WindowStartUtc.ToString("O", CultureInfo.InvariantCulture));
        sb.Append("Captured at (UTC): ").AppendLine(snapshot.CapturedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        sb.AppendLine();
        sb.AppendLine("WheaErrorCount (event IDs 17,18,19,46): " + snapshot.WheaErrorCount.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("FatalWheaCount (18): " + snapshot.FatalWheaCount.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("CorrectedWheaCount (17, 19): " + snapshot.CorrectedWheaCount.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("OtherWheaCount (46): " + snapshot.OtherWheaCount.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("GpuDriverResetCount (Display / 4101): " + snapshot.GpuDriverResetCount.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("DangerLevel: " + _dangerState.Level);
        sb.AppendLine("DangerReason: " + _dangerState.Reason);
        sb.AppendLine("DangerReasonDetail: " + _dangerState.ReasonDetail);
        sb.AppendLine("DangerRollbackApplied: " + _dangerState.AutoRollbackApplied);
        sb.AppendLine();
        sb.AppendLine("--- WHEA events in window ---");
        foreach (WheaEventRecord e in snapshot.WheaEvents)
        {
            sb.AppendLine();
            string timeStr = e.TimeCreated.HasValue
                ? e.TimeCreated.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
                : "(null)";
            sb.AppendLine("  Time (UTC): " + timeStr);
            sb.AppendLine("  Id: " + e.Id.ToString(CultureInfo.InvariantCulture) + "  Level: " + e.Level);
            sb.AppendLine("  Provider: " + e.Provider);
            if (!string.IsNullOrEmpty(e.Component)) sb.AppendLine("  Component: " + e.Component);
            if (!string.IsNullOrEmpty(e.ErrorSource)) sb.AppendLine("  Error source: " + e.ErrorSource);
            if (!string.IsNullOrEmpty(e.ErrorType)) sb.AppendLine("  Error type: " + e.ErrorType);
            if (!string.IsNullOrEmpty(e.ProcessorApicId)) sb.AppendLine("  Processor APIC ID: " + e.ProcessorApicId);
            sb.AppendLine("  Message: " + e.Message);
        }

        sb.AppendLine();
        sb.AppendLine("--- GPU driver recovery events in window ---");
        foreach (GpuDriverResetEvent e in snapshot.GpuDriverResets)
        {
            sb.AppendLine();
            string timeStr = e.TimeCreated.HasValue
                ? e.TimeCreated.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
                : "(null)";
            sb.AppendLine("  Time (UTC): " + timeStr);
            sb.AppendLine("  Id: " + e.Id.ToString(CultureInfo.InvariantCulture) + "  Level: " + e.Level);
            sb.AppendLine("  Provider: " + e.Provider);
            sb.AppendLine("  Message: " + e.Message);
        }

        if (_dangerAudit.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("--- Danger audit trail ---");
            foreach (string entry in _dangerAudit)
            {
                sb.AppendLine(entry);
            }
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
    }

    private void LoadRunStateAndMarkStarted()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_runStateFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(_runStateFilePath))
            {
                string json = File.ReadAllText(_runStateFilePath, Encoding.UTF8);
                RunStateMarker? marker = JsonSerializer.Deserialize<RunStateMarker>(json);
                _previousRunUnclean = marker?.IsRunning == true;
            }

            MarkRunState(cleanShutdown: false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Run-state load failed: {ex.Message}");
        }
    }

    private void MarkRunState(bool cleanShutdown)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_runStateFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var marker = new RunStateMarker
            {
                IsRunning = !cleanShutdown,
                LastStartUtc = _sessionStartUtc,
                LastUpdateUtc = DateTime.UtcNow,
                LastCleanShutdownUtc = cleanShutdown ? DateTime.UtcNow : null
            };

            string json = JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_runStateFilePath, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Run-state save failed: {ex.Message}");
        }
    }

    private sealed class RunStateMarker
    {
        public bool IsRunning { get; set; }
        public DateTime LastStartUtc { get; set; }
        public DateTime LastUpdateUtc { get; set; }
        public DateTime? LastCleanShutdownUtc { get; set; }
    }

#if DEBUG
    private bool _debugDangerInjected;

    private void ProcessDebugDangerSimulation()
    {
        if (_debugDangerInjected)
        {
            return;
        }

        string? mode = Environment.GetEnvironmentVariable("DYNOTUNE_SIM_DANGER");
        if (string.IsNullOrWhiteSpace(mode))
        {
            return;
        }

        _debugDangerInjected = true;
        DangerReason reason = mode.Trim().ToUpperInvariant() switch
        {
            "WHEA" => DangerReason.WheaEvent,
            "GPU" => DangerReason.GpuDriverReset,
            "CRASH" => DangerReason.AppCrashDetected,
            _ => DangerReason.None
        };

        if (reason == DangerReason.None)
        {
            return;
        }

        TriggerDanger(reason, $"Debug simulation trigger: {mode}", DateTime.UtcNow, DangerLevel.Critical);
    }
#endif
}
