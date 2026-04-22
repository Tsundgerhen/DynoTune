namespace DynoTune.Models;

/// <summary>
/// One fan sensor reading from LibreHardwareMonitor (motherboard/SuperIO).
/// GPU fan comes separately via ADLX in GpuMetrics.
/// </summary>
public class FanInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Measured rotations per minute.</summary>
    public double Rpm { get; set; }

    /// <summary>Duty-cycle percentage if the board exposes a control sensor for this fan.</summary>
    public double? SpeedPercent { get; set; }
}
