namespace DynoTune.Models;

public class HwinfoCpuTelemetry
{
    public double? TemperatureC { get; set; }
    public double? ClockMHz { get; set; }
    public double? PackagePowerW { get; set; }
    public double? CpuFanRpm { get; set; }
    public double? CpuFanPercent { get; set; }
    public IReadOnlyList<FanInfo> AdditionalSystemFans { get; set; } = Array.Empty<FanInfo>();

    public bool HasTemperature => TemperatureC.HasValue;
    public bool HasClock => ClockMHz.HasValue && ClockMHz.Value > 0;
    public bool HasPower => PackagePowerW.HasValue && PackagePowerW.Value > 0;
    public bool HasFan => CpuFanRpm.HasValue && CpuFanRpm.Value > 0;
}
