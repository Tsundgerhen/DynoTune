namespace DynoTune.Models;

/// <summary>
/// Built-in Windows power schemes (GUID-backed in <see cref="Services.WindowsPowerPlanService"/>).
/// </summary>
public enum WindowsPowerPlanKind
{
    Balanced = 0,
    PowerSaver = 1,
    HighPerformance = 2
}
