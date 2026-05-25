namespace DynoTune.Models;

public enum OptimizationPhase
{
    Idle = 0,
    BaselineCaptured = 1,
    Recommending = 2,
    Applying = 3,
    Monitoring = 4,
    RolledBack = 5
}
