using System.Management;
using DynoTune.Models;
using LibreHardwareMonitor.Hardware;

namespace DynoTune.Services;

public class LibreHardwareMonitorService
{
    // Priority order for CPU temperature sensor name matching.
    // Earlier entries win when multiple valid candidates are found.
    private static readonly string[] CpuTempPriorityNames =
    [
        "Tctl", "Tdie", "CPU Package", "Package",
        "CPU Socket", "Socket", "CPU", "TMPIN0", "TMPIN1", "TMPIN2",
        "Temp1", "Temp2", "Temp3"
    ];

    private readonly Computer _computer;
    private readonly UpdateVisitor _updateVisitor = new();
    private bool _hasDumpedSensorInventory;
    private bool _hasLoggedFallbackSummary;

    // Guards against calling Accept() twice in the same timer tick.
    private DateTime _lastUpdateTime = DateTime.MinValue;

    private int? _lastLoggedFanCount;

    public LibreHardwareMonitorService()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsMotherboardEnabled = true,
            IsMemoryEnabled = false,
            IsGpuEnabled = false,
            IsStorageEnabled = false,
            IsNetworkEnabled = false,
            IsControllerEnabled = true
        };
    }

    public void Initialize()
    {
        _computer.Open();
    }

    public void Shutdown()
    {
        _computer.Close();
    }

    /// <summary>
    /// Reads all fan sensors from motherboard, SuperIO, Embedded Controller, and CPU nodes.
    /// Returns ALL detected fan sensor slots — including those at 0 RPM (passive/off fans).
    /// Call after GetCpuMetrics() — reuses the same Accept() within 500 ms.
    /// </summary>
    public IReadOnlyList<FanInfo> GetFanReadings()
    {
        EnsureUpdated();

        var fans = new List<FanInfo>();
        foreach (IHardware hardware in _computer.Hardware)
        {
            CollectFansRecursive(hardware, fans);
        }

        // Sort: spinning fans first, then passive/off ones.
        fans.Sort((a, b) => b.Rpm.CompareTo(a.Rpm));

        if (_lastLoggedFanCount != fans.Count)
        {
            _lastLoggedFanCount = fans.Count;
            System.Diagnostics.Debug.WriteLine(
                $"[Fans] {fans.Count} sensor(s) detected: " +
                string.Join(", ", fans.ConvertAll(f => $"{f.Name}={f.Rpm:F0}RPM")));
        }

        return fans;
    }

    private static void CollectFansRecursive(IHardware hardware, List<FanInfo> fans)
    {
        var rpmSensors = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
        var pctSensors = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (ISensor sensor in hardware.Sensors)
        {
            string name = sensor.Name ?? string.Empty;

            if (sensor.SensorType == SensorType.Fan)
            {
                // Always add fan sensors — even null/0 means the slot is wired or passive.
                rpmSensors[name] = sensor.Value.HasValue ? (double?)sensor.Value.Value : null;
            }
            else if (sensor.SensorType == SensorType.Control && sensor.Value.HasValue)
            {
                pctSensors[name] = sensor.Value.Value;
            }
        }

        foreach (var (name, rpm) in rpmSensors)
        {
            // Try to find a matching Control sensor (names often differ, e.g. "Fan #1" vs "Fan Control #1").
            double? pct = null;
            if (pctSensors.TryGetValue(name, out double exactPct))
            {
                pct = exactPct > 0 ? exactPct : null;
            }
            else
            {
                // Fuzzy match: pick a Control sensor whose name contains the fan's index token.
                foreach (var (ctrlName, ctrlPct) in pctSensors)
                {
                    if (ctrlPct > 0 && (ctrlName.Contains(name, StringComparison.OrdinalIgnoreCase)
                                       || name.Contains(ctrlName, StringComparison.OrdinalIgnoreCase)))
                    {
                        pct = ctrlPct;
                        break;
                    }
                }
            }

            fans.Add(new FanInfo
            {
                Name = name,
                Rpm = rpm ?? 0.0,
                SpeedPercent = pct
            });
        }

        foreach (IHardware sub in hardware.SubHardware)
        {
            CollectFansRecursive(sub, fans);
        }
    }

    // Calls Accept() at most once per 500 ms so that GetCpuMetrics() and
    // GetFanReadings() called back-to-back don't double-poll the hardware.
    private void EnsureUpdated()
    {
        if ((DateTime.UtcNow - _lastUpdateTime).TotalMilliseconds > 500)
        {
            _computer.Accept(_updateVisitor);
            _lastUpdateTime = DateTime.UtcNow;
        }
    }

    public CpuMetrics GetCpuMetrics()
    {
        EnsureUpdated();

        double usagePercent = 0;
        double clockMHz = 0;
        double? powerW = null;
        double? packagePowerW = null;
        bool thermalThrottle = false;
        bool powerThrottle = false;
        bool usageFound = false;
        bool clockFound = false;
        double clockSum = 0;
        int clockCount = 0;
        bool packagePowerFound = false;
        bool anyPowerFound = false;
        double? cpuFanRpm = null;
        double? cpuFanPercent = null;
        string cpuName = string.Empty;

        foreach (IHardware hardware in _computer.Hardware)
        {
            // Only collect non-temperature metrics from CPU node.
            if (hardware.HardwareType == HardwareType.Cpu)
            {
                if (string.IsNullOrEmpty(cpuName))
                    cpuName = hardware.Name ?? string.Empty;
                void ProcessCpuSensor(ISensor sensor)
                {
                    if (sensor.Value is null)
                    {
                        return;
                    }

                    float value = sensor.Value.Value;
                    string sensorName = sensor.Name ?? string.Empty;

                    bool isNonLoadMetric =
                        sensor.SensorType == SensorType.Clock ||
                        sensor.SensorType == SensorType.Power ||
                        sensor.SensorType == SensorType.Factor;
                    if (isNonLoadMetric && value <= 0)
                    {
                        return;
                    }

                    if (sensor.SensorType == SensorType.Load)
                    {
                        if (sensorName.Contains("Total", StringComparison.OrdinalIgnoreCase))
                        {
                            usagePercent = value;
                            usageFound = true;
                        }
                        else if (!usageFound && value > usagePercent)
                        {
                            usagePercent = value;
                        }
                    }
                    else if (sensor.SensorType == SensorType.Clock)
                    {
                        if (sensorName.Contains("Core", StringComparison.OrdinalIgnoreCase))
                        {
                            clockSum += value;
                            clockCount++;
                            clockFound = true;
                        }
                        else if (!clockFound && value > clockMHz)
                        {
                            clockMHz = value;
                        }
                    }
                    else if (sensor.SensorType == SensorType.Power)
                    {
                        anyPowerFound = true;
                        if (sensorName.Contains("Package", StringComparison.OrdinalIgnoreCase))
                        {
                            packagePowerW = value;
                            packagePowerFound = true;
                            powerW = value;
                        }
                        else if (!packagePowerFound)
                        {
                            powerW = (powerW ?? 0) + value;
                        }
                    }
                    else if (sensor.SensorType == SensorType.Factor)
                    {
                        if (sensorName.Contains("Thermal", StringComparison.OrdinalIgnoreCase))
                        {
                            thermalThrottle = value > 0;
                        }
                        else if (sensorName.Contains("Power", StringComparison.OrdinalIgnoreCase))
                        {
                            powerThrottle = value > 0;
                        }
                    }
                }

                ProcessHardwareSensorsRecursive(hardware, ProcessCpuSensor);
            }

            if (cpuFanRpm is null)
            {
                TryCollectCpuFanRecursive(hardware, ref cpuFanRpm, ref cpuFanPercent);
            }
        }

        string clockSource = "Unavailable";
        string tempSource = "Unavailable";
        string powerSource = "Unavailable";
        string fanSource = "Unavailable";

        if (!anyPowerFound)
        {
            packagePowerW = null;
            powerW = null;
        }
        else
        {
            powerSource = "LHM";
        }

        if (clockCount > 0)
        {
            clockMHz = clockSum / clockCount;
            clockSource = "LHM";
        }

        // WMI fallback for clock: Win32_Processor.CurrentClockSpeed works without admin.
        if (clockMHz <= 0)
        {
            clockMHz = TryGetCpuClockMhzViaWmi();
            if (clockMHz > 0)
            {
                clockSource = "WMI";
            }
        }

        // Temperature uses a separate multi-hardware scan with fallback priority.
        double? temperatureC = FindBestCpuTemperature();
        if (temperatureC.HasValue)
        {
            tempSource = "LHM";
        }
        else if (TryGetCpuTemperatureViaWmi(out double wmiTemp))
        {
            temperatureC = wmiTemp;
            tempSource = "WMI";
        }

        if (cpuFanRpm.HasValue)
        {
            fanSource = "LHM";
        }

        var notes = new List<string>();
        if (temperatureC is null)
        {
            notes.Add("CPU temperature unsupported");
        }
        if (clockMHz <= 0)
        {
            notes.Add("CPU clock unsupported");
        }
        if (powerW is null && packagePowerW is null)
        {
            notes.Add("CPU power unsupported");
        }
        if (cpuFanRpm is null)
        {
            notes.Add("CPU fan unsupported");
        }

        if (!_hasLoggedFallbackSummary)
        {
            _hasLoggedFallbackSummary = true;
            System.Diagnostics.Debug.WriteLine(
                $"[CPU] src temp={tempSource}, clock={clockSource}, power={powerSource}, fan={fanSource}");
        }

        return new CpuMetrics
        {
            Name = cpuName,
            UsagePercent = usagePercent,
            TemperatureC = temperatureC,
            ClockMHz = clockMHz,
            PowerW = powerW,
            PackagePowerW = packagePowerW,
            CpuFanRpm = cpuFanRpm,
            CpuFanPercent = cpuFanPercent,
            TemperatureSource = tempSource,
            ClockSource = clockSource,
            PowerSource = powerSource,
            FanSource = fanSource,
            HasTemperature = temperatureC.HasValue,
            HasClock = clockMHz > 0,
            HasPower = powerW.HasValue || packagePowerW.HasValue,
            HasCpuFan = cpuFanRpm.HasValue,
            AvailabilityNote = string.Join("; ", notes),
            IsThermallyThrottling = thermalThrottle,
            IsPowerThrottling = powerThrottle
        };
    }

    // Scans CPU, Motherboard, and SuperIO hardware for the best CPU temperature candidate.
    // Priority order: Ryzen internal > CPU-named > Socket/Package-named > TMPIN/Temp fallbacks.
    private double? FindBestCpuTemperature()
    {
        // Collect all valid temperature sensor candidates across CPU + motherboard nodes.
        var candidates = new List<(ISensor Sensor, int Priority)>();

        foreach (IHardware hardware in _computer.Hardware)
        {
            bool isThermalHardware =
                hardware.HardwareType == HardwareType.Cpu ||
                hardware.HardwareType == HardwareType.Motherboard ||
                hardware.HardwareType.ToString().Contains("Super", StringComparison.OrdinalIgnoreCase) ||
                hardware.HardwareType.ToString().Contains("Embedded", StringComparison.OrdinalIgnoreCase);

            if (!isThermalHardware)
            {
                continue;
            }

            CollectTempCandidatesRecursive(hardware, candidates);
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        // Return highest-priority (lowest priority number) candidate.
        candidates.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        return candidates[0].Sensor.Value.HasValue ? (double?)candidates[0].Sensor.Value!.Value : null;
    }

    private static void CollectTempCandidatesRecursive(
        IHardware hardware,
        List<(ISensor, int)> candidates)
    {
        foreach (ISensor sensor in hardware.Sensors)
        {
            if (sensor.SensorType != SensorType.Temperature)
            {
                continue;
            }

            if (!sensor.Value.HasValue)
            {
                continue;
            }

            float value = sensor.Value.Value;

            // Reject physically implausible readings.
            if (value < 5 || value > 120)
            {
                continue;
            }

            string name = sensor.Name ?? string.Empty;
            int priority = GetCpuTempPriority(name);

            // Only include sensors that match a known CPU-like pattern.
            if (priority < int.MaxValue)
            {
                candidates.Add((sensor, priority));
            }
        }

        foreach (IHardware subHardware in hardware.SubHardware)
        {
            CollectTempCandidatesRecursive(subHardware, candidates);
        }
    }

    private static int GetCpuTempPriority(string sensorName)
    {
        for (int i = 0; i < CpuTempPriorityNames.Length; i++)
        {
            if (sensorName.Contains(CpuTempPriorityNames[i], StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private static double TryGetCpuClockMhzViaWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT CurrentClockSpeed FROM Win32_Processor");

            double total = 0;
            int count = 0;
            foreach (ManagementObject obj in searcher.Get())
            {
                if (obj["CurrentClockSpeed"] is uint speed && speed > 0)
                {
                    total += speed;
                    count++;
                }
            }

            return count > 0 ? total / count : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static bool TryGetCpuTemperatureViaWmi(out double temperatureC)
    {
        temperatureC = 0;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");

            foreach (ManagementObject obj in searcher.Get())
            {
                if (obj["CurrentTemperature"] is uint raw && raw > 0)
                {
                    // WMI ACPI value is tenths of Kelvin.
                    double c = raw / 10.0 - 273.15;
                    if (c >= 10 && c <= 115)
                    {
                        temperatureC = c;
                        return true;
                    }
                }
            }
        }
        catch
        {
            // Ignore - fallback is best-effort only.
        }

        return false;
    }

    private static void TryCollectCpuFanRecursive(IHardware hardware, ref double? rpm, ref double? percent)
    {
        foreach (ISensor sensor in hardware.Sensors)
        {
            string sensorName = sensor.Name ?? string.Empty;
            bool isCpuLike = sensorName.Contains("CPU", StringComparison.OrdinalIgnoreCase)
                             || sensorName.Contains("Processor", StringComparison.OrdinalIgnoreCase);
            if (!isCpuLike)
            {
                continue;
            }

            if (sensor.SensorType == SensorType.Fan && sensor.Value.HasValue && sensor.Value.Value > 0)
            {
                rpm = sensor.Value.Value;
            }
            else if (sensor.SensorType == SensorType.Control && sensor.Value.HasValue && sensor.Value.Value > 0)
            {
                percent = sensor.Value.Value;
            }
        }

        foreach (IHardware sub in hardware.SubHardware)
        {
            TryCollectCpuFanRecursive(sub, ref rpm, ref percent);
        }
    }

    public void DumpSensorInventoryOnce()
    {
        if (_hasDumpedSensorInventory)
        {
            return;
        }

        _computer.Accept(_updateVisitor);
        _hasDumpedSensorInventory = true;

        System.Diagnostics.Debug.WriteLine("===== LHM Sensor Inventory (one-time) =====");
        foreach (IHardware hardware in _computer.Hardware)
        {
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
