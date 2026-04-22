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
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DynoTune;

[SupportedOSPlatform("windows10.0.19041.0")]
public sealed partial class MainWindow : Window
{
    // ── Hardware services ─────────────────────────────────────────────────────
    private readonly AmdAdlxService _gpuService = new();
    private readonly LibreHardwareMonitorService _cpuService = new();
    private readonly MonitoringService _monitoringService;
    private readonly LoggingService _loggingService;
    private readonly WorkloadClassifier _workloadClassifier = new();
    private readonly WindowsPowerPlanService _powerPlanService = new();
    private readonly ProfileService _profileService;
    private readonly StabilityMonitorService _stabilityMonitor = new();

    // ── Session state ─────────────────────────────────────────────────────────
    private readonly DateTime _sessionStartUtc = DateTime.UtcNow;
    private readonly DispatcherTimer _loggingTimer = new();
    private int _tickCount;
    private bool _isShuttingDown;

    // Stability is checked every 60 ticks to avoid blocking the UI thread frequently.
    private const int StabilityCheckIntervalTicks = 60;

    private string? _lastClassifierLogKey;

    public MainWindow()
    {
        InitializeComponent();

        _monitoringService = new MonitoringService(_cpuService, _gpuService);
        _loggingService = new LoggingService(_monitoringService);
        _profileService = new ProfileService(_powerPlanService);

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
        }

        _loggingTimer.Interval = TimeSpan.FromSeconds(1);
        _loggingTimer.Tick += LoggingTimer_Tick;
        _loggingTimer.Start();
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

            SensorSnapshot snapshot = _monitoringService.GetCurrentSnapshot();
            ClassificationResult classification = _workloadClassifier.Classify(snapshot);
            string powerPlanLabel = GetPowerPlanLabel();

            // Log to CSV record.
            LogRecord record = _loggingService.CreateRecordFromSnapshot(snapshot, "Stock", classification);
            _loggingService.AddRecord(record);

            // Push live data to the UI ViewModel.
            App.LiveData.Update(snapshot, classification, powerPlanLabel);

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
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Stability check failed: {ex.Message}");
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
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Export failed: {ex.Message}");
        }

        _gpuService.Shutdown();
        _cpuService.Shutdown();
    }

    private async Task SaveStabilitySessionLogAsync(string filePath)
    {
        StabilitySnapshot snapshot = _stabilityMonitor.GetSnapshotSince(_sessionStartUtc);

        var sb = new StringBuilder();
        sb.AppendLine("DynoTune stability session log");
        sb.AppendLine("Counts are from the Windows System event log (WHEA-Logger and Display 4101), not raw hardware registers.");
        sb.AppendLine();
        sb.Append("Window start (UTC): ").AppendLine(snapshot.WindowStartUtc.ToString("O", CultureInfo.InvariantCulture));
        sb.Append("Captured at (UTC): ").AppendLine(snapshot.CapturedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        sb.AppendLine();
        sb.AppendLine("WheaErrorCount (event IDs 17,18,19,46): " + snapshot.WheaErrorCount.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("FatalWheaCount (18): " + snapshot.FatalWheaCount.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("CorrectedWheaCount (17, 19): " + snapshot.CorrectedWheaCount.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("OtherWheaCount (46): " + snapshot.OtherWheaCount.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("GpuDriverResetCount (Display / 4101): " + snapshot.GpuDriverResetCount.ToString(CultureInfo.InvariantCulture));
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

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
    }
}
