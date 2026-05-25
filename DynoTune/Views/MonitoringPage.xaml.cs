using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using DynoTune.Models;
using DynoTune.Services;
using DynoTune.ViewModels;

namespace DynoTune.Views
{
    [SupportedOSPlatform("windows10.0.19041.0")]
    public sealed partial class MonitoringPage : Page
    {
        private static MonitoringViewModel VM => App.LiveData;

        // Static so history survives page re-creation on navigation.
        private static readonly Queue<(double Value, bool IsOptimizing)> _cpuHistory = new();
        private static readonly Queue<(double Value, bool IsOptimizing)> _gpuHistory = new();
        private const int HistoryCapacity = 60;

        // Static so the fan-row rebuild guard survives page re-creation on navigation.
        private static int _lastFanCount = -1;

        // ── Smooth animation ──────────────────────────────────────────────────
        private readonly DispatcherTimer _animTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
        private bool _snapOnNextFrame = true;

        // Target values — written at 1 Hz from the VM on each Refreshed event.
        private double _tCpuUsage;
        private double _tGpuUsage, _tGpuTemp, _tGpuHotspot, _tGpuPower;
        private double _tGpuCoreClock, _tGpuMemClock, _tGpuFanRpm, _tGpuFanPct, _tGpuVoltage;
        private double _tMemUsedGb, _tMemTotalGb, _tSysPower;

        // Display values — interpolated toward targets each animation frame.
        private double _dCpuUsage;
        private double _dGpuUsage, _dGpuTemp, _dGpuHotspot, _dGpuPower;
        private double _dGpuCoreClock, _dGpuMemClock, _dGpuFanRpm, _dGpuFanPct, _dGpuVoltage;
        private double _dMemUsedGb, _dMemTotalGb, _dSysPower;

        // Sparkline colours: active = optimization running, inactive = stopped.
        private static readonly Windows.UI.Color CpuActiveColor =
            Windows.UI.Color.FromArgb(255, 77, 130, 245);
        private static readonly Windows.UI.Color GpuActiveColor =
            Windows.UI.Color.FromArgb(255, 52, 199, 89);
        private static readonly Windows.UI.Color InactiveColor =
            Windows.UI.Color.FromArgb(255, 230, 65, 50);

        public MonitoringPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Pre-populate from DB if queues are empty (fresh app start / first load).
            if (_cpuHistory.Count == 0 && App.TelemetryRepo != null)
            {
                foreach (TelemetrySample s in App.TelemetryRepo.GetRecent(HistoryCapacity))
                {
                    EnqueueHistory(_cpuHistory, s.CpuUsagePct, s.IsOptimizing);
                    EnqueueHistory(_gpuHistory, s.GpuUsagePct, s.IsOptimizing);
                }
            }

            VM.Refreshed += OnVmRefreshed;
            _animTimer.Tick += OnAnimTick;
            _animTimer.Start();
            CpuSparklineCanvas.SizeChanged += OnSparklineCanvasSizeChanged;
            GpuSparklineCanvas.SizeChanged += OnSparklineCanvasSizeChanged;
            OnVmRefreshed(this, EventArgs.Empty);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            VM.Refreshed -= OnVmRefreshed;
            _animTimer.Stop();
            _animTimer.Tick -= OnAnimTick;
            CpuSparklineCanvas.SizeChanged -= OnSparklineCanvasSizeChanged;
            GpuSparklineCanvas.SizeChanged -= OnSparklineCanvasSizeChanged;
            _snapOnNextFrame = true;
        }

        private void OnSparklineCanvasSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.NewSize.Width > 0)
                RedrawSparklines();
        }

        private void OnVmRefreshed(object? sender, EventArgs e)
        {
            UpdateTargets();
            UpdateElevationBanner();
            UpdateCpuCard();
            UpdateGpuCard();
            UpdateSystemCard();
            UpdateFansCard();
            UpdateStabilityBar();
            UpdateSparklines();
        }

        private void UpdateElevationBanner()
        {
            if (VM.RunningElevated)
            {
                ElevationBanner.Background = (Brush)Application.Current.Resources["SystemFillColorSuccessBackgroundBrush"];
                ElevationBanner.BorderBrush = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
                ElevationBannerText.Text =
                    "Running as Administrator — LibreHardwareMonitor can use ring-0 drivers (CPU temp, clocks, fans when the board exposes them).";
                ElevationBannerText.Opacity = 0.95;
            }
            else
            {
                ElevationBanner.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
                ElevationBanner.BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
                ElevationBannerText.Text =
                    "Not elevated — CPU temp/clocks/fans via LibreHardwareMonitor usually need Administrator. "
                    + "Packaged app: register AppxManifest.xml, then start with explorer shell:AppsFolder\\a11f19c2-0e96-4a2d-835e-334b4c09b9d0_5gyrq6psz227t!App (do not RunAs the AppX folder .exe — it will not start). "
                    + "For RunAs + UAC, build unpackaged: dotnet build -p:DynoTuneUnpackaged=true -p:UseAdminApplicationManifest=true, then Start-Process …\\win-x64\\DynoTune.exe -Verb RunAs.";
                ElevationBannerText.Opacity = 1;
            }
        }

        // ── CPU card ──────────────────────────────────────────────────────────

        private void UpdateCpuCard()
        {
            if (!string.IsNullOrEmpty(VM.CpuName))
            {
                CpuNameText.Text = VM.CpuName;
                CpuNameText.Visibility = Visibility.Visible;
            }

            CpuTempText.Text = VM.CpuHasTemperature && VM.CpuTemperatureC.HasValue
                ? FormatWithSource($"{VM.CpuTemperatureC.Value:F0} °C", VM.CpuTemperatureSource)
                : UnavailableText();

            CpuPowerText.Text = VM.CpuPowerW.HasValue
                ? FormatWithSource($"{VM.CpuPowerW.Value:F0} W", VM.CpuPowerSource)
                : UnavailableText();

            CpuPackagePowerText.Text = VM.CpuPackagePowerW.HasValue
                ? FormatWithSource($"{VM.CpuPackagePowerW.Value:F0} W", VM.CpuPowerSource)
                : UnavailableText();

            CpuClockText.Text = VM.CpuHasClock && VM.CpuClockMHz > 0
                ? FormatWithSource($"{VM.CpuClockMHz:F0} MHz", VM.CpuClockSource)
                : UnavailableText();

            string cpuFanDisplay = UnavailableText();
            if (VM.CpuHasFan && VM.CpuFanRpm.HasValue)
            {
                cpuFanDisplay = VM.CpuFanPercent.HasValue
                    ? $"{VM.CpuFanRpm.Value:F0} RPM  ({VM.CpuFanPercent.Value:F0} %) [{VM.CpuFanSource}]"
                    : $"{VM.CpuFanRpm.Value:F0} RPM [{VM.CpuFanSource}]";
            }
            else
            {
                // Legacy fallback from fan list if CPU-tagged fan exists.
                foreach (FanInfo fan in VM.SystemFans)
                {
                    if (!fan.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    cpuFanDisplay = fan.SpeedPercent.HasValue
                        ? $"{fan.Rpm:F0} RPM  ({fan.SpeedPercent.Value:F0} %) [LHM]"
                        : $"{fan.Rpm:F0} RPM [LHM]";
                    break;
                }
            }
            CpuFanText.Text = cpuFanDisplay;

            bool throttling = VM.CpuIsThrottling;
            CpuThrottleText.Visibility = throttling ? Visibility.Visible : Visibility.Collapsed;
            CpuThrottleText.Text = throttling ? "⚠ Throttling" : string.Empty;

            CpuUsageBar.Foreground = VM.CpuUsagePercent >= 90
                ? new SolidColorBrush(Colors.OrangeRed)
                : (SolidColorBrush)Application.Current.Resources["AccentAAFillColorDefaultBrush"];

            CpuMinFreqText.Text = VM.CpuMinFrequencyPercent.HasValue
                ? $"{VM.CpuMinFrequencyPercent.Value} %"
                : "--";
            CpuMaxFreqText.Text = VM.CpuMaxFrequencyPercent.HasValue
                ? $"{VM.CpuMaxFrequencyPercent.Value} %"
                : "--";
            CpuBoostModeText.Text = VM.CpuBoostMode.HasValue
                ? FormatBoostMode(VM.CpuBoostMode.Value)
                : "--";
        }

        private static string FormatBoostMode(ProcessorBoostMode mode) => mode switch
        {
            ProcessorBoostMode.Disabled             => "Disabled",
            ProcessorBoostMode.Enabled              => "Enabled",
            ProcessorBoostMode.Aggressive           => "Aggressive",
            ProcessorBoostMode.EfficientEnabled     => "Efficient Enabled",
            ProcessorBoostMode.EfficientAggressive  => "Efficient Aggressive",
            _                                       => mode.ToString()
        };

        private string UnavailableText() =>
            VM.RunningElevated ? "Unsupported on this board" : "Not elevated";

        private static string FormatWithSource(string valueText, string source)
        {
            if (string.IsNullOrWhiteSpace(source) || source.Equals("LHM", StringComparison.OrdinalIgnoreCase))
            {
                return valueText;
            }

            return $"{valueText} [{source}]";
        }

        // ── GPU card ──────────────────────────────────────────────────────────

        private void UpdateGpuCard()
        {
            if (!string.IsNullOrEmpty(VM.GpuName))
            {
                GpuNameText.Text = VM.GpuName;
            }

            GpuVramText.Text = VM.GpuVramUsageMb.HasValue
                ? $"{VM.GpuVramUsageMb.Value:F0} MB" : "--";

            GpuVoltageText.Text = VM.GpuVoltageMv.HasValue
                ? $"{VM.GpuVoltageMv.Value:F0} mV" : "--";

            bool throttling = VM.GpuIsThrottling;
            GpuThrottleText.Visibility = throttling ? Visibility.Visible : Visibility.Collapsed;
            GpuThrottleText.Text = throttling ? "⚠ Throttling" : string.Empty;

            GpuUsageBar.Foreground = VM.GpuUsagePercent >= 90
                ? new SolidColorBrush(Colors.YellowGreen)
                : new SolidColorBrush(Colors.MediumSeaGreen);
        }

        // ── System card ───────────────────────────────────────────────────────

        private void UpdateSystemCard()
        {
            WorkloadTypeText.Text = VM.WorkloadTypeName;
            WorkloadReasonText.Text = VM.ClassificationReason;
            PowerPlanText.Text = VM.PowerPlanLabel;

            WheaCountText.Text = VM.WheaErrorCount.ToString(CultureInfo.InvariantCulture);
            GpuResetCountText.Text = VM.GpuResetCount.ToString(CultureInfo.InvariantCulture);

            WorkloadBadgeText.Text = VM.WorkloadTypeName;
            PowerPlanBadgeText.Text = VM.PowerPlanLabel;

            WorkloadBadgeBorder.Background = VM.WorkloadTypeName switch
            {
                "Gaming"       => new SolidColorBrush(Windows.UI.Color.FromArgb(40, 52, 199, 89)),
                "HeavyCompute" => new SolidColorBrush(Windows.UI.Color.FromArgb(40, 255, 149, 0)),
                "Idle"         => new SolidColorBrush(Windows.UI.Color.FromArgb(40, 142, 142, 147)),
                _              => new SolidColorBrush(Windows.UI.Color.FromArgb(40, 77, 130, 245))
            };

            TimestampText.Text = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }

        // ── Fans card ─────────────────────────────────────────────────────────

        private void UpdateFansCard()
        {
            IReadOnlyList<FanInfo> fans = VM.SystemFans;

            if (fans.Count == 0)
            {
                FansEmptyText.Text = VM.RunningElevated
                    ? "No fan sensors from LibreHardwareMonitor — with Administrator enabled, this usually means your board's SuperIO/EC is not supported (or not exposed) in this LHM version, not a permission problem. Compare with the standalone LibreHardwareMonitor app; see Debug output for the one-time sensor inventory."
                    : "No fan sensors — run elevated (unpackaged DynoTune.exe with RunAs, or install runtime + admin) so LHM can access the SuperIO driver when the board supports it.";
                FansEmptyText.Visibility = Visibility.Visible;
            }
            else
            {
                FansEmptyText.Visibility = Visibility.Collapsed;
            }

            if (fans.Count != _lastFanCount)
            {
                // Rebuild the panel when the number of fans detected changes.
                FansPanel.Children.Clear();
                _lastFanCount = fans.Count;

                foreach (FanInfo _ in fans)
                {
                    FansPanel.Children.Add(MakeFanRow());
                }
            }

            // Update text in existing rows.
            for (int i = 0; i < fans.Count && i < FansPanel.Children.Count; i++)
            {
                if (FansPanel.Children[i] is Grid row)
                {
                    SetFanRowText(row, fans[i]);
                }
            }
        }

        private static Grid MakeFanRow()
        {
            var row = new Grid
            {
                Margin = new Thickness(0, 3, 0, 3),
                ColumnSpacing = 12
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var nameBlock = new TextBlock
            {
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var rpmBlock = new TextBlock
            {
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            var bar = new ProgressBar
            {
                Maximum = 100,
                Height = 4,
                CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(180, 100, 180, 255))
            };

            Grid.SetColumn(rpmBlock, 1);
            Grid.SetColumn(bar, 2);
            row.Children.Add(nameBlock);
            row.Children.Add(rpmBlock);
            row.Children.Add(bar);

            return row;
        }

        private static void SetFanRowText(Grid row, FanInfo fan)
        {
            bool isPassive = fan.Rpm <= 0;

            if (row.Children[0] is TextBlock nameBlock)
            {
                nameBlock.Text = fan.Name;
                nameBlock.Opacity = isPassive ? 0.45 : 1.0;
            }

            if (row.Children[1] is TextBlock rpmBlock)
            {
                if (isPassive)
                {
                    rpmBlock.Text = "0 RPM (passive / off)";
                    rpmBlock.Opacity = 0.45;
                }
                else
                {
                    rpmBlock.Text = fan.SpeedPercent.HasValue
                        ? $"{fan.Rpm:F0} RPM  ({fan.SpeedPercent.Value:F0} %)"
                        : $"{fan.Rpm:F0} RPM";
                    rpmBlock.Opacity = 1.0;
                }
            }

            if (row.Children[2] is ProgressBar bar)
            {
                bar.Value = fan.SpeedPercent.HasValue
                    ? Math.Clamp(fan.SpeedPercent.Value, 0, 100)
                    : Math.Clamp(fan.Rpm / 3000.0 * 100.0, 0, 100);
                bar.Opacity = isPassive ? 0.3 : 1.0;
            }
        }

        // ── Stability bar ─────────────────────────────────────────────────────

        private void UpdateStabilityBar()
        {
            bool stable = VM.DangerLevel == DangerLevel.Safe;

            StabilityStatusText.Text = stable ? "Session stable" : "Issues detected";
            StabilityIcon.Glyph = stable ? "\uE73E" : "\uE7BA";
            StabilityIcon.Foreground = stable
                ? new SolidColorBrush(Colors.MediumSeaGreen)
                : new SolidColorBrush(Colors.OrangeRed);

            if (stable)
            {
                StabilityDetailText.Text = "No WHEA errors · No GPU resets";
            }
            else
            {
                string rollbackText = VM.DangerRollbackApplied ? " · Safe rollback applied" : string.Empty;
                StabilityDetailText.Text =
                    $"{VM.DangerReason}: {VM.DangerReasonDetail}{rollbackText}";
            }

            TimeSpan elapsed = DateTime.UtcNow - VM.SessionStartUtc;
            SessionDurationText.Text = elapsed.TotalHours >= 1
                ? $"Session: {(int)elapsed.TotalHours}h {elapsed.Minutes:D2}m"
                : $"Session: {elapsed.Minutes}m {elapsed.Seconds:D2}s";
        }

        // ── Sparklines ────────────────────────────────────────────────────────

        private void UpdateSparklines()
        {
            bool isOptimizing = App.OptimizationService?.SessionState.IsRunning ?? false;
            EnqueueHistory(_cpuHistory, VM.CpuUsagePercent, isOptimizing);
            EnqueueHistory(_gpuHistory, VM.GpuUsagePercent, isOptimizing);
            RedrawSparklines();
        }

        private void RedrawSparklines()
        {
            DrawSparkline(CpuSparklineCanvas, _cpuHistory, CpuActiveColor, InactiveColor, maxValue: 100.0);
            DrawSparkline(GpuSparklineCanvas, _gpuHistory, GpuActiveColor, InactiveColor, maxValue: 100.0);
        }

        private static void EnqueueHistory(Queue<(double Value, bool IsOptimizing)> queue, double value, bool isOptimizing)
        {
            queue.Enqueue((value, isOptimizing));
            while (queue.Count > HistoryCapacity)
            {
                queue.Dequeue();
            }
        }

        // ── Smooth animation methods ──────────────────────────────────────────

        private void UpdateTargets()
        {
            _tCpuUsage = VM.CpuUsagePercent;

            _tGpuUsage    = VM.GpuUsagePercent;
            _tGpuTemp     = VM.GpuTemperatureC;
            _tGpuHotspot  = VM.GpuHotspotTemperatureC ?? _tGpuHotspot;
            _tGpuPower    = VM.GpuPowerW;
            _tGpuCoreClock = VM.GpuCoreClock > 0 ? VM.GpuCoreClock : _tGpuCoreClock;
            _tGpuMemClock  = VM.GpuMemoryClock > 0 ? VM.GpuMemoryClock : _tGpuMemClock;
            _tGpuFanRpm    = VM.GpuFanRpm > 0 ? VM.GpuFanRpm : _tGpuFanRpm;
            _tGpuFanPct    = VM.GpuFanPercent ?? _tGpuFanPct;
            _tGpuVoltage   = VM.GpuVoltageMv ?? _tGpuVoltage;

            _tMemUsedGb  = VM.MemoryUsedGB;
            _tMemTotalGb = VM.MemoryTotalGB;
            _tSysPower   = VM.SystemPowerW ?? _tSysPower;

            if (_snapOnNextFrame)
            {
                _dCpuUsage = _tCpuUsage;
                _dGpuUsage = _tGpuUsage; _dGpuTemp = _tGpuTemp; _dGpuHotspot = _tGpuHotspot;
                _dGpuPower = _tGpuPower; _dGpuCoreClock = _tGpuCoreClock; _dGpuMemClock = _tGpuMemClock;
                _dGpuFanRpm = _tGpuFanRpm; _dGpuFanPct = _tGpuFanPct; _dGpuVoltage = _tGpuVoltage;
                _dMemUsedGb = _tMemUsedGb; _dMemTotalGb = _tMemTotalGb; _dSysPower = _tSysPower;
                _snapOnNextFrame = false;
            }
        }

        private void OnAnimTick(object? sender, object e)
        {
            const double a = 0.20;
            _dCpuUsage     += (_tCpuUsage     - _dCpuUsage)     * a;
            _dGpuUsage     += (_tGpuUsage     - _dGpuUsage)     * a;
            _dGpuTemp      += (_tGpuTemp      - _dGpuTemp)      * a;
            _dGpuHotspot   += (_tGpuHotspot   - _dGpuHotspot)   * a;
            _dGpuPower     += (_tGpuPower     - _dGpuPower)     * a;
            _dGpuCoreClock += (_tGpuCoreClock - _dGpuCoreClock) * a;
            _dGpuMemClock  += (_tGpuMemClock  - _dGpuMemClock)  * a;
            _dGpuFanRpm    += (_tGpuFanRpm    - _dGpuFanRpm)    * a;
            _dGpuFanPct    += (_tGpuFanPct    - _dGpuFanPct)    * a;
            _dGpuVoltage   += (_tGpuVoltage   - _dGpuVoltage)   * a;
            _dMemUsedGb    += (_tMemUsedGb    - _dMemUsedGb)    * a;
            _dMemTotalGb   += (_tMemTotalGb   - _dMemTotalGb)   * a;
            _dSysPower     += (_tSysPower     - _dSysPower)     * a;
            DrawAnimatedMetrics();
        }

        private void DrawAnimatedMetrics()
        {
            // CPU
            CpuUsageText.Text = $"{_dCpuUsage:F0} %";
            CpuUsageBar.Value = _dCpuUsage;

            // GPU
            GpuUsageText.Text = $"{_dGpuUsage:F0} %";
            GpuUsageBar.Value = _dGpuUsage;

            string tempDisplay = $"{_dGpuTemp:F0} °C";
            if (VM.GpuHotspotTemperatureC.HasValue)
                tempDisplay += $"  (hs {_dGpuHotspot:F0})";
            GpuTempText.Text = tempDisplay;

            GpuPowerText.Text      = $"{_dGpuPower:F0} W";
            GpuCoreClockText.Text  = _dGpuCoreClock > 1 ? $"{_dGpuCoreClock:F0} MHz" : "--";
            GpuMemClockText.Text   = _dGpuMemClock  > 1 ? $"{_dGpuMemClock:F0} MHz"  : "--";
            GpuFanText.Text        = _dGpuFanRpm    > 1 ? $"{_dGpuFanRpm:F0} RPM"    : "--";
            GpuFanPercentText.Text = VM.GpuFanPercent.HasValue ? $"{_dGpuFanPct:F0} %"  : "--";
            GpuVoltageText.Text    = VM.GpuVoltageMv.HasValue  ? $"{_dGpuVoltage:F0} mV" : "--";

            // System
            double memPct = _dMemTotalGb > 0.1 ? _dMemUsedGb / _dMemTotalGb * 100.0 : 0;
            RamUsageText.Text    = $"{_dMemUsedGb:F1} / {_dMemTotalGb:F1} GB";
            RamUsageBar.Value    = memPct;
            SystemPowerText.Text = VM.SystemPowerW.HasValue ? $"{_dSysPower:F0} W" : "--";
        }

        private static void DrawSparkline(
            Canvas canvas,
            Queue<(double Value, bool IsOptimizing)> history,
            Windows.UI.Color activeColor,
            Windows.UI.Color inactiveColor,
            double maxValue)
        {
            canvas.Children.Clear();

            if (history.Count < 2)
                return;

            double w = canvas.ActualWidth > 0 ? canvas.ActualWidth : 300;
            double h = canvas.ActualHeight > 0 ? canvas.ActualHeight : 64;
            double pad = 3.0;

            var items = history.ToArray();
            int count = items.Length;
            double xStep = w / Math.Max(count - 1, 1);

            // Precompute screen coordinates for every sample
            var pts = new (Point Pt, bool IsOptimizing)[count];
            for (int i = 0; i < count; i++)
            {
                double x = i * xStep;
                double normalized = Math.Clamp(items[i].Value / maxValue, 0.0, 1.0);
                double y = h - pad - normalized * (h - pad * 2);
                pts[i] = (new Point(x, y), items[i].IsOptimizing);
            }

            // Unified fill polygon (dim inactive color as background tint)
            var fillColor = Windows.UI.Color.FromArgb(25, inactiveColor.R, inactiveColor.G, inactiveColor.B);
            var polygon = new Polygon { Fill = new SolidColorBrush(fillColor), StrokeThickness = 0 };
            polygon.Points.Add(new Point(0, h));
            foreach (var (pt, _) in pts)
                polygon.Points.Add(pt);
            polygon.Points.Add(new Point((count - 1) * xStep, h));
            canvas.Children.Add(polygon);

            // Segmented polylines — one per consecutive run of the same optimization state
            int seg = 0;
            while (seg < count)
            {
                bool state = pts[seg].IsOptimizing;
                var polyline = new Polyline
                {
                    Stroke = new SolidColorBrush(state ? activeColor : inactiveColor),
                    StrokeThickness = 1.8,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };

                int end = seg;
                while (end < count && pts[end].IsOptimizing == state)
                    end++;

                for (int i = seg; i < end; i++)
                    polyline.Points.Add(pts[i].Pt);

                // Overlap one point into the next segment for visual continuity at transitions
                if (end < count)
                    polyline.Points.Add(pts[end].Pt);

                canvas.Children.Add(polyline);
                seg = end;
            }

            // 50 % guide line
            double midY = h - pad - 0.5 * (h - pad * 2);
            canvas.Children.Add(new Line
            {
                X1 = 0, Y1 = midY, X2 = w, Y2 = midY,
                Stroke = new SolidColorBrush(
                    Windows.UI.Color.FromArgb(25, activeColor.R, activeColor.G, activeColor.B)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 4 }
            });
        }
    }
}
