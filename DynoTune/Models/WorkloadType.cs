namespace DynoTune.Models;

/// <summary>
/// Fine-grained workload label from rule-based classification (telemetry snapshot).
/// </summary>
public enum WorkloadType
{
    Unknown = 0,
    Idle = 1,
    Browsing = 2,
    Office = 3,
    Media = 4,
    Gaming = 5,
    HeavyCompute = 6
}
