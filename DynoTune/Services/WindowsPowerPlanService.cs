using System.Runtime.InteropServices;
using DynoTune.Models;

namespace DynoTune.Services;

/// <summary>
/// Reads and sets the active Windows power plan via powrprof (Balanced / Power saver / High performance).
/// Changing the active scheme may require administrator rights.
/// </summary>
public class WindowsPowerPlanService
{
    // Well-known scheme GUIDs (same on all Windows locales).
    public static readonly Guid BalancedScheme = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid PowerSaverScheme = Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a");
    public static readonly Guid HighPerformanceScheme = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    public bool TryGetActiveScheme(out Guid activeSchemeGuid)
    {
        activeSchemeGuid = Guid.Empty;
        uint err = PowerGetActiveScheme(IntPtr.Zero, out IntPtr ptr);
        if (err != 0 || ptr == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            activeSchemeGuid = Marshal.PtrToStructure<Guid>(ptr);
            return true;
        }
        finally
        {
            _ = LocalFree(ptr);
        }
    }

    public bool TryGetActivePlanKind(out WindowsPowerPlanKind kind)
    {
        kind = WindowsPowerPlanKind.Balanced;
        if (!TryGetActiveScheme(out Guid g))
        {
            return false;
        }

        if (g == PowerSaverScheme)
        {
            kind = WindowsPowerPlanKind.PowerSaver;
        }
        else if (g == HighPerformanceScheme)
        {
            kind = WindowsPowerPlanKind.HighPerformance;
        }
        else if (g == BalancedScheme)
        {
            kind = WindowsPowerPlanKind.Balanced;
        }
        else
        {
            return false;
        }

        return true;
    }

    public bool TrySetActivePlan(WindowsPowerPlanKind kind)
    {
        Guid g = kind switch
        {
            WindowsPowerPlanKind.PowerSaver => PowerSaverScheme,
            WindowsPowerPlanKind.HighPerformance => HighPerformanceScheme,
            _ => BalancedScheme
        };

        return PowerSetActiveScheme(IntPtr.Zero, ref g) == 0;
    }

    public static Guid GuidForKind(WindowsPowerPlanKind kind) => kind switch
    {
        WindowsPowerPlanKind.PowerSaver => PowerSaverScheme,
        WindowsPowerPlanKind.HighPerformance => HighPerformanceScheme,
        _ => BalancedScheme
    };

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
