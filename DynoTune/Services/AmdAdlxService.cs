using System.Text;
using DynoTune.Models;
using LibreHardwareMonitor.Hardware;

namespace DynoTune.Services;

public class AmdAdlxService
{
    private ADLXHelper? _helper;
    private readonly Computer _computer = new()
    {
        IsGpuEnabled = true
    };
    private readonly UpdateVisitor _updateVisitor = new();
    private bool _hardwareOpened;
    private bool _hasDumpedSensorInventory;

    public bool Initialize()
    {
        if (_helper is not null)
        {
            return true;
        }

        ADLX_RESULT result;
        try
        {
            // Sample pattern: create ADLXHelper, then call Initialize.
            _helper = new ADLXHelper();
            result = _helper.Initialize();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ADLX init exception: {ex.Message}");
            _helper = null;
            return false;
        }

        if (!_hardwareOpened)
        {
            try
            {
                _computer.Open();
                _hardwareOpened = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GPU hardware monitor open failed: {ex.Message}");
            }
        }

        if (result == ADLX_RESULT.ADLX_OK)
        {
            return true;
        }

        _helper = null;
        return false;
    }

    public void Shutdown()
    {
        if (_helper is not null)
        {
            try
            {
                // Sample pattern: terminate through the same helper instance.
                _helper.Terminate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ADLX terminate exception: {ex.Message}");
            }
            _helper = null;
        }

        if (_hardwareOpened)
        {
            try
            {
                _computer.Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GPU hardware monitor close failed: {ex.Message}");
            }
            _hardwareOpened = false;
        }
    }

    public bool TryApplyUndervoltCandidate(
        int? gpuVoltageMv,
        int? gpuMaxClockMHz,
        int? gpuPowerLimitPercent,
        out string reason)
    {
        try
        {
            if (gpuVoltageMv is null && gpuMaxClockMHz is null && gpuPowerLimitPercent is null)
            {
                reason = "No GPU tuning values requested.";
                return true;
            }

            if (_helper is null)
            {
                reason = "ADLX helper unavailable — GPU parameters not applied.";
                System.Diagnostics.Debug.WriteLine($"[ADLX] TryApplyUndervoltCandidate skipped: helper null. " +
                    $"V={gpuVoltageMv} mV, Clock={gpuMaxClockMHz} MHz, PowerLimit={gpuPowerLimitPercent}%");
                return false;
            }

            // Log all requested values for traceability.
            System.Diagnostics.Debug.WriteLine($"[ADLX] TryApplyUndervoltCandidate requested: " +
                $"V={gpuVoltageMv} mV, Clock={gpuMaxClockMHz} MHz, PowerLimit={gpuPowerLimitPercent}%");

            bool anyApplied = false;
            var reasonParts = new System.Text.StringBuilder();

            // GPU power limit: attempt via IADLXSystem tuning services if ADLX is initialized.
            // The ADLX C# binding does not yet expose a typed PowerTuning API in generated bindings,
            // so this is the safe partial-apply gate: log the attempt and record partial success.
            if (gpuPowerLimitPercent.HasValue)
            {
                // Safe gate: ADLX GPU tuning for power limit requires IADLXGPUTuningServices.
                // Until the generated binding exposes a validated write path, we log the intent
                // and report partial apply so the caller can make an informed decision.
                System.Diagnostics.Debug.WriteLine($"[ADLX] PowerLimit={gpuPowerLimitPercent}% — write path not yet exposed in bindings (partial apply).");
                reasonParts.Append($"PowerLimit={gpuPowerLimitPercent}% logged (binding write path pending). ");
                anyApplied = true;
            }

            // Voltage and clock: direct write is guarded until ADLX tuning interface is validated.
            if (gpuVoltageMv.HasValue || gpuMaxClockMHz.HasValue)
            {
                System.Diagnostics.Debug.WriteLine($"[ADLX] V/Clock write path guarded. " +
                    $"V={gpuVoltageMv} mV, Clock={gpuMaxClockMHz} MHz — not applied.");
                reasonParts.Append($"V/Clock write guarded (safe gate). ");
            }

            if (anyApplied)
            {
                reason = reasonParts.ToString().TrimEnd();
                // Return false because no actual hardware state change was made.
                // The caller (ProfileSearchService) will reject and log accordingly.
                return false;
            }

            reason = "No GPU parameters could be applied in this build.";
            return false;
        }
        catch (Exception ex)
        {
            reason = $"GPU tuning apply exception: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[ADLX] TryApplyUndervoltCandidate exception: {ex}");
            return false;
        }
    }

    public GpuMetrics GetGpuMetrics()
    {
        if (_hardwareOpened)
        {
            _computer.Accept(_updateVisitor);
        }

        string gpuName = "AMD GPU";
        if (_helper is not null)
        {
            try
            {
                string adlxVersion = _helper.QueryVersion();
                gpuName = string.IsNullOrWhiteSpace(adlxVersion) ? "AMD GPU" : $"AMD GPU (ADLX {adlxVersion})";
            }
            catch
            {
                gpuName = "AMD GPU (ADLX unavailable)";
            }
        }

        var metrics = new GpuMetrics
        {
            Name = gpuName
        };
        double maxLoad = 0;
        bool usageMatchedByName = false;
        bool usageMatchedByD3d = false;
        bool memoryClockMatchedByName = false;

        foreach (IHardware hardware in _computer.Hardware)
        {
            if (hardware.HardwareType != HardwareType.GpuAmd)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(hardware.Name))
            {
                metrics.Name = hardware.Name;
            }

            void ProcessSensor(ISensor sensor)
            {
                if (sensor.Value is null)
                {
                    return;
                }

                float value = sensor.Value.Value;
                string sensorName = sensor.Name ?? string.Empty;

                switch (sensor.SensorType)
                {
                    case SensorType.Load:
                        if (value > maxLoad)
                        {
                            maxLoad = value;
                        }

                        // Prefer D3D 3D engine load over generic "GPU Core" load, which
                        // can be 0 on some drivers while other engines are active.
                        if (sensorName.Contains("D3D 3D", StringComparison.OrdinalIgnoreCase))
                        {
                            metrics.UsagePercent = value;
                            usageMatchedByD3d = true;
                            usageMatchedByName = true;
                        }
                        else if (!usageMatchedByD3d &&
                                 (sensorName.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                                  sensorName.Contains("GPU", StringComparison.OrdinalIgnoreCase) ||
                                  sensorName.Contains("3D", StringComparison.OrdinalIgnoreCase)))
                        {
                            if (value > metrics.UsagePercent)
                            {
                                metrics.UsagePercent = value;
                            }
                            usageMatchedByName = true;
                        }
                        break;
                    case SensorType.Temperature:
                        if (sensorName.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase) ||
                            sensorName.Contains("Hotspot", StringComparison.OrdinalIgnoreCase))
                        {
                            metrics.HotspotTemperatureC = value;
                        }
                        else if (sensorName.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                                 sensorName.Contains("GPU", StringComparison.OrdinalIgnoreCase))
                        {
                            metrics.TemperatureC = value;
                        }
                        else if (metrics.TemperatureC <= 0)
                        {
                            metrics.TemperatureC = value;
                        }
                        break;
                    case SensorType.Clock:
                        if (sensorName.Contains("Memory", StringComparison.OrdinalIgnoreCase) ||
                            sensorName.Contains("VRAM", StringComparison.OrdinalIgnoreCase))
                        {
                            metrics.MemoryClockMHz = value;
                            memoryClockMatchedByName = true;
                        }
                        else if (sensorName.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                                 sensorName.Contains("GPU", StringComparison.OrdinalIgnoreCase))
                        {
                            metrics.CoreClockMHz = value;
                        }
                        else if (!memoryClockMatchedByName && value > metrics.MemoryClockMHz)
                        {
                            // Fallback for labels like "Mem" or vendor-specific clock naming.
                            metrics.MemoryClockMHz = value;
                        }
                        break;
                    case SensorType.Power:
                        if (sensorName.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                            sensorName.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                            sensorName.Contains("GPU", StringComparison.OrdinalIgnoreCase))
                        {
                            metrics.PowerW = value;
                        }
                        break;
                    case SensorType.Fan:
                        metrics.FanRpm = (int)Math.Round(value);
                        break;
                    case SensorType.Control:
                        metrics.FanPercent = value;
                        break;
                    case SensorType.SmallData:
                    case SensorType.Data:
                        if (sensorName.Contains("D3D Dedicated Memory Used", StringComparison.OrdinalIgnoreCase))
                        {
                            metrics.VramUsageMb = value;
                        }
                        else if (sensorName.Contains("Memory Used", StringComparison.OrdinalIgnoreCase) ||
                            sensorName.Contains("VRAM Used", StringComparison.OrdinalIgnoreCase))
                        {
                            metrics.VramUsageMb = value;
                        }
                        break;
                    case SensorType.Voltage:
                        if (sensorName.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                            sensorName.Contains("GPU", StringComparison.OrdinalIgnoreCase))
                        {
                            metrics.VoltageMv = value * 1000.0;
                        }
                        break;
                }
            }

            ProcessHardwareSensorsRecursive(hardware, ProcessSensor);
        }

        if (!usageMatchedByName)
        {
            metrics.UsagePercent = maxLoad;
        }

        metrics.IsThrottling = metrics.HotspotTemperatureC is >= 95 || metrics.TemperatureC >= 85;
        return metrics;
    }

    public void DumpSensorInventoryOnce()
    {
        if (_hasDumpedSensorInventory || !_hardwareOpened)
        {
            return;
        }

        _computer.Accept(_updateVisitor);
        _hasDumpedSensorInventory = true;

        System.Diagnostics.Debug.WriteLine("===== GPU Sensor Inventory (one-time) =====");
        foreach (IHardware hardware in _computer.Hardware)
        {
            if (hardware.HardwareType != HardwareType.GpuAmd)
            {
                continue;
            }

            DumpHardwareRecursive(hardware, 0);
        }
        System.Diagnostics.Debug.WriteLine("===========================================");
    }

    private static void DumpHardwareRecursive(IHardware hardware, int depth)
    {
        string indent = new string(' ', depth * 2);
        System.Diagnostics.Debug.WriteLine($"{indent}[HW] {hardware.HardwareType} :: {hardware.Name}");

        foreach (ISensor sensor in hardware.Sensors)
        {
            string sensorName = sensor.Name ?? string.Empty;
            string value = sensor.Value?.ToString() ?? "null";
            System.Diagnostics.Debug.WriteLine($"{indent}  [SENSOR] {sensor.SensorType} :: {sensorName} = {value}");
        }

        foreach (IHardware subHardware in hardware.SubHardware)
        {
            DumpHardwareRecursive(subHardware, depth + 1);
        }
    }

    private static void ProcessHardwareSensorsRecursive(IHardware hardware, Action<ISensor> processSensor)
    {
        foreach (ISensor sensor in hardware.Sensors)
        {
            processSensor(sensor);
        }

        foreach (IHardware subHardware in hardware.SubHardware)
        {
            ProcessHardwareSensorsRecursive(subHardware, processSensor);
        }
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer)
        {
            foreach (IHardware hardware in computer.Hardware)
            {
                hardware.Accept(this);
            }
        }

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (IHardware subHardware in hardware.SubHardware)
            {
                subHardware.Accept(this);
            }
        }

        public void VisitParameter(IParameter parameter)
        {
        }

        public void VisitSensor(ISensor sensor)
        {
        }
    }
}