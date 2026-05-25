namespace DynoTune.Models;

public enum DangerReason
{
    None = 0,
    WheaEvent = 1,
    GpuDriverReset = 2,
    AppCrashDetected = 3,
    ManualRollback = 4
}
