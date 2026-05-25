using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using DynoTune.ViewModels;

namespace DynoTune.Views;

[SupportedOSPlatform("windows10.0.19041.0")]
public sealed partial class DemoPage : Page
{
    private static MonitoringViewModel VM => App.LiveData;
    private const int HistoryCapacity = 7200; // 2 h at 1 Hz

    private readonly Random _rng = new();

    // Static so history survives page re-creation on navigation.
    private static bool _optimizationEverStarted = false;
    private static readonly Queue<(double Value, bool IsRunning)> _gpuPower = new();
    private static readonly Queue<(double Value, bool IsRunning)> _cpuPower = new();
    private static readonly Queue<(double Value, bool IsRunning)> _gpuTemp  = new();
    private static readonly Queue<(double Value, bool IsRunning)> _cpuTemp  = new();

    // Unoptimized offsets — re-rolled only when the real sensor value changes (integer resolution).
    private static int _lastGpuPowerRaw = int.MinValue;
    private static int _lastCpuPowerRaw = int.MinValue;
    private static int _lastGpuTempRaw  = int.MinValue;
    private static int _lastCpuTempRaw  = int.MinValue;
    private static int _gpuPowerOffset  = 15;
    private static int _cpuPowerOffset  = 15;
    private static int _gpuTempOffset   = 7;
    private static int _cpuTempOffset   = 7;

    private static readonly Windows.UI.Color PowerColor   = Windows.UI.Color.FromArgb(255, 77, 130, 245);
    private static readonly Windows.UI.Color TempColor    = Windows.UI.Color.FromArgb(255, 255, 149, 0);
    private static readonly Windows.UI.Color StoppedColor = Windows.UI.Color.FromArgb(255, 220, 70, 70);

    public DemoPage()
    {
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        VM.Refreshed += OnRefreshed;
        GpuPowerCanvas.SizeChanged += OnCanvasSizeChanged;
        CpuPowerCanvas.SizeChanged += OnCanvasSizeChanged;
        GpuTempCanvas.SizeChanged  += OnCanvasSizeChanged;
        CpuTempCanvas.SizeChanged  += OnCanvasSizeChanged;
        OnRefreshed(this, EventArgs.Empty);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        VM.Refreshed -= OnRefreshed;
        GpuPowerCanvas.SizeChanged -= OnCanvasSizeChanged;
        CpuPowerCanvas.SizeChanged -= OnCanvasSizeChanged;
        GpuTempCanvas.SizeChanged  -= OnCanvasSizeChanged;
        CpuTempCanvas.SizeChanged  -= OnCanvasSizeChanged;
    }

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0)
            RedrawCharts();
    }

    private void OnRefreshed(object? sender, EventArgs e)
    {
        double gpuPowerRaw = VM.GpuPowerW;
        double gpuTempRaw  = VM.GpuTemperatureC;
        double cpuPowerRaw = VM.CpuPowerW ?? 0.0;
        double cpuTempRaw  = VM.CpuTemperatureC ?? 0.0;

        bool isRunning = App.OptimizationService?.SessionState.IsRunning ?? false;
        if (isRunning) _optimizationEverStarted = true;

        // Before optimization is ever started, show normal color (not red)
        bool displayAsRunning = isRunning || !_optimizationEverStarted;

        double gpuPower, gpuTemp, cpuPower, cpuTemp;
        if (isRunning)
        {
            gpuPower = gpuPowerRaw;
            gpuTemp  = gpuTempRaw;
            cpuPower = cpuPowerRaw;
            cpuTemp  = cpuTempRaw;
        }
        else
        {
            // Re-roll offset only when the integer part of the real sensor value changes.
            if ((int)gpuPowerRaw != _lastGpuPowerRaw) { _gpuPowerOffset = _rng.Next(10, 21); _lastGpuPowerRaw = (int)gpuPowerRaw; }
            if ((int)cpuPowerRaw != _lastCpuPowerRaw) { _cpuPowerOffset = _rng.Next(10, 21); _lastCpuPowerRaw = (int)cpuPowerRaw; }
            if ((int)gpuTempRaw  != _lastGpuTempRaw)  { _gpuTempOffset  = _rng.Next(5, 11);  _lastGpuTempRaw  = (int)gpuTempRaw;  }
            if ((int)cpuTempRaw  != _lastCpuTempRaw)  { _cpuTempOffset  = _rng.Next(5, 11);  _lastCpuTempRaw  = (int)cpuTempRaw;  }

            gpuPower = (int)(gpuPowerRaw * (1.0 + _gpuPowerOffset / 100.0));
            gpuTemp  = (int)(gpuTempRaw  * (1.0 + _gpuTempOffset  / 100.0));
            cpuPower = (int)(cpuPowerRaw * (1.0 + _cpuPowerOffset / 100.0));
            cpuTemp  = (int)(cpuTempRaw  * (1.0 + _cpuTempOffset  / 100.0));
        }

        Enqueue(_gpuPower, (gpuPower, displayAsRunning));
        Enqueue(_cpuPower, (cpuPower, displayAsRunning));
        Enqueue(_gpuTemp,  (gpuTemp,  displayAsRunning));
        Enqueue(_cpuTemp,  (cpuTemp,  displayAsRunning));

        RedrawCharts();

        GpuPowerCurrentValue.Text = $"{(int)gpuPower} W";
        CpuPowerCurrentValue.Text = cpuPowerRaw > 0 ? $"{(int)cpuPower} W"  : "--";
        GpuTempCurrentValue.Text  = $"{(int)gpuTemp} °C";
        CpuTempCurrentValue.Text  = cpuTempRaw  > 0 ? $"{(int)cpuTemp} °C" : "--";
    }

    private void RedrawCharts()
    {
        DrawChart(GpuPowerCanvas, _gpuPower, PowerColor, maxValue: 150.0);
        DrawChart(CpuPowerCanvas, _cpuPower, PowerColor, maxValue: 120.0);
        DrawChart(GpuTempCanvas,  _gpuTemp,  TempColor,  maxValue: 100.0);
        DrawChart(CpuTempCanvas,  _cpuTemp,  TempColor,  maxValue: 100.0);
    }

    private static void Enqueue(Queue<(double Value, bool IsRunning)> queue, (double Value, bool IsRunning) item)
    {
        queue.Enqueue(item);
        while (queue.Count > HistoryCapacity)
            queue.Dequeue();
    }

    private static void DrawChart(
        Canvas canvas,
        Queue<(double Value, bool IsRunning)> history,
        Windows.UI.Color runningColor,
        double maxValue)
    {
        canvas.Children.Clear();

        if (history.Count < 2)
            return;

        double w   = canvas.ActualWidth  > 0 ? canvas.ActualWidth  : 300;
        double h   = canvas.ActualHeight > 0 ? canvas.ActualHeight : 140;
        double pad = 3.0;

        var items  = history.ToArray();
        int count  = items.Length;
        double xStep = w / Math.Max(count - 1, 1);

        // Unified semi-transparent fill under the whole line
        var fillColor = Windows.UI.Color.FromArgb(25, runningColor.R, runningColor.G, runningColor.B);
        var polygon = new Polygon { Fill = new SolidColorBrush(fillColor), StrokeThickness = 0 };
        polygon.Points.Add(new Point(0, h));
        for (int i = 0; i < count; i++)
        {
            double normalized = Math.Clamp(items[i].Value / maxValue, 0.0, 1.0);
            polygon.Points.Add(new Point(i * xStep, h - pad - normalized * (h - pad * 2)));
        }
        polygon.Points.Add(new Point((count - 1) * xStep, h));
        canvas.Children.Add(polygon);

        // Segmented polylines — color switches at each optimization state change
        Polyline? seg = null;
        bool? lastState = null;

        for (int i = 0; i < count; i++)
        {
            double x = i * xStep;
            double normalized = Math.Clamp(items[i].Value / maxValue, 0.0, 1.0);
            double y = h - pad - normalized * (h - pad * 2);
            bool running = items[i].IsRunning;

            if (lastState != running)
            {
                // Carry last point into new segment so there is no visible gap
                Point? bridge = seg?.Points.Count > 0 ? seg.Points[^1] : (Point?)null;

                var color = running ? runningColor : StoppedColor;
                seg = new Polyline
                {
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = 1.8,
                    StrokeLineJoin     = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap   = PenLineCap.Round
                };
                if (bridge.HasValue)
                    seg.Points.Add(bridge.Value);
                canvas.Children.Add(seg);
                lastState = running;
            }

            seg!.Points.Add(new Point(x, y));
        }

        // Mid guide line
        double midY = h - pad - 0.5 * (h - pad * 2);
        canvas.Children.Add(new Line
        {
            X1 = 0, Y1 = midY, X2 = w, Y2 = midY,
            Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(20, runningColor.R, runningColor.G, runningColor.B)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 4 }
        });
    }
}
