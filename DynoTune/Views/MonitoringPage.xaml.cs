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
using DynoTune.ViewModels;

namespace DynoTune.Views
{
    [SupportedOSPlatform("windows10.0.19041.0")]
    public sealed partial class MonitoringPage : Page
    {
        private static MonitoringViewModel VM => App.LiveData;

        // Rolling 60-sample history for sparklines.
        private readonly Queue<double> _cpuHistory = new();
        private readonly Queue<double> _gpuHistory = new();
        private const int HistoryCapacity = 60;

        // Tracks how many fan rows are in FansPanel so we only rebuild on change.
        private int _lastFanCount = -1;

        // Sparkline colours.
        private static readonly Windows.UI.Color CpuColor =
            Windows.UI.Color.FromArgb(255, 77, 130, 245);
        private static readonly Windows.UI.Color GpuColor =
            Windows.UI.Color.FromArgb(255, 52, 199, 89);

        public MonitoringPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            VM.Refreshed += OnVmRefreshed;
            OnVmRefreshed(this, EventArgs.Empty);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            VM.Refreshed -= OnVmRefreshed;
        }

        private void OnVmRefreshed(object? sender, EventArgs e)
        {
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
            CpuUsageText.Text = $"{VM.CpuUsagePercent:F0} %";
            CpuUsageBar.Value = VM.CpuUsagePercent;

            CpuTempText.Text = VM.CpuHasTemperature && VM.CpuTemperatureC.HasValue
                ? FormatWithSource($"{VM.CpuTemperatureC.Value:F0} °C", VM.CpuTemperatureSource)
                : "Unsupported on this board";

            CpuPowerText.Text = VM.CpuPowerW.HasValue
                ? FormatWithSource($"{VM.CpuPowerW.Value:F0} W", VM.CpuPowerSource)
                : "Unsupported on this board";

            CpuPackagePowerText.Text = VM.CpuPackagePowerW.HasValue
                ? FormatWithSource($"{VM.CpuPackagePowerW.Value:F0} W", VM.CpuPowerSource)
                : "Unsupported on this board";

            CpuClockText.Text = VM.CpuHasClock && VM.CpuClockMHz > 0
                ? FormatWithSource($"{VM.CpuClockMHz:F0} MHz", VM.CpuClockSource)
                : "Unsupported on this board";

            string cpuFanDisplay = "Unsupported on this board";
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
        }

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

            GpuUsageText.Text = $"{VM.GpuUsagePercent:F0} %";
            GpuUsageBar.Value = VM.GpuUsagePercent;

            // Temperature — show hotspot in parentheses when available.
            string tempDisplay = $"{VM.GpuTemperatureC:F0} °C";
            if (VM.GpuHotspotTemperatureC.HasValue)
            {
                tempDisplay += $"  (hs {VM.GpuHotspotTemperatureC.Value:F0})";
            }
            GpuTempText.Text = tempDisplay;

            GpuPowerText.Text = $"{VM.GpuPowerW:F0} W";

            GpuCoreClockText.Text = VM.GpuCoreClock > 0
                ? $"{VM.GpuCoreClock:F0} MHz" : "--";

            GpuMemClockText.Text = VM.GpuMemoryClock > 0
                ? $"{VM.GpuMemoryClock:F0} MHz" : "--";

            GpuFanText.Text = VM.GpuFanRpm > 0
                ? $"{VM.GpuFanRpm} RPM" : "--";

            GpuFanPercentText.Text = VM.GpuFanPercent.HasValue
                ? $"{VM.GpuFanPercent.Value:F0} %" : "--";

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
            double ramUsed = VM.MemoryUsedGB;
            double ramTotal = VM.MemoryTotalGB;
            RamUsageText.Text = $"{ramUsed:F1} / {ramTotal:F1} GB";
            RamUsageBar.Value = ramTotal > 0.1 ? ramUsed / ramTotal * 100.0 : 0;

            SystemPowerText.Text = VM.SystemPowerW.HasValue
                ? $"{VM.SystemPowerW.Value:F0} W" : "--";

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
            bool stable = VM.WheaErrorCount == 0 && VM.GpuResetCount == 0;

            StabilityStatusText.Text = stable ? "Session stable" : "Issues detected";
            StabilityIcon.Glyph = stable ? "\uE73E" : "\uE7BA";
            StabilityIcon.Foreground = stable
                ? new SolidColorBrush(Colors.MediumSeaGreen)
                : new SolidColorBrush(Colors.OrangeRed);

            StabilityDetailText.Text = stable
                ? "No WHEA errors · No GPU resets"
                : $"WHEA events: {VM.WheaErrorCount}  ·  GPU resets: {VM.GpuResetCount}";

            TimeSpan elapsed = DateTime.UtcNow - VM.SessionStartUtc;
            SessionDurationText.Text = elapsed.TotalHours >= 1
                ? $"Session: {(int)elapsed.TotalHours}h {elapsed.Minutes:D2}m"
                : $"Session: {elapsed.Minutes}m {elapsed.Seconds:D2}s";
        }

        // ── Sparklines ────────────────────────────────────────────────────────

        private void UpdateSparklines()
        {
            EnqueueHistory(_cpuHistory, VM.CpuUsagePercent);
            EnqueueHistory(_gpuHistory, VM.GpuUsagePercent);

            DrawSparkline(CpuSparklineCanvas, _cpuHistory, CpuColor, maxValue: 100.0);
            DrawSparkline(GpuSparklineCanvas, _gpuHistory, GpuColor, maxValue: 100.0);
        }

        private static void EnqueueHistory(Queue<double> queue, double value)
        {
            queue.Enqueue(value);
            while (queue.Count > HistoryCapacity)
            {
                queue.Dequeue();
            }
        }

        private static void DrawSparkline(
            Canvas canvas,
            Queue<double> history,
            Windows.UI.Color lineColor,
            double maxValue)
        {
            canvas.Children.Clear();

            if (history.Count < 2)
            {
                return;
            }

            double w = canvas.ActualWidth > 0 ? canvas.ActualWidth : 300;
            double h = canvas.ActualHeight > 0 ? canvas.ActualHeight : 64;
            double pad = 3.0;

            double[] values = history.ToArray();
            int count = values.Length;
            double xStep = w / Math.Max(count - 1, 1);

            var fillColor = Windows.UI.Color.FromArgb(35, lineColor.R, lineColor.G, lineColor.B);

            var polygon = new Polygon { Fill = new SolidColorBrush(fillColor), StrokeThickness = 0 };
            var polyline = new Polyline
            {
                Stroke = new SolidColorBrush(lineColor),
                StrokeThickness = 1.8,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };

            polygon.Points.Add(new Point(0, h));

            for (int i = 0; i < count; i++)
            {
                double x = i * xStep;
                double normalized = Math.Clamp(values[i] / maxValue, 0.0, 1.0);
                double y = h - pad - normalized * (h - pad * 2);

                var pt = new Point(x, y);
                polyline.Points.Add(pt);
                polygon.Points.Add(pt);
            }

            polygon.Points.Add(new Point((count - 1) * xStep, h));

            canvas.Children.Add(polygon);
            canvas.Children.Add(polyline);

            // 50 % guide line.
            double midY = h - pad - 0.5 * (h - pad * 2);
            canvas.Children.Add(new Line
            {
                X1 = 0, Y1 = midY, X2 = w, Y2 = midY,
                Stroke = new SolidColorBrush(
                    Windows.UI.Color.FromArgb(25, lineColor.R, lineColor.G, lineColor.B)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 4 }
            });
        }
    }
}
