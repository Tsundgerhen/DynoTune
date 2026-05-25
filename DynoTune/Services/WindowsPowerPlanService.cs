using System.Runtime.InteropServices;
using DynoTune.Models;

namespace DynoTune.Services;

/// <summary>
/// Reads and sets the active Windows power plan via powrprof (Balanced / Power saver / High performance).
/// Also reads and writes granular CPU power management settings (min/max frequency, boost mode).
/// Changing scheme or writing settings may require administrator rights.
/// </summary>
public class WindowsPowerPlanService
{
    // Well-known scheme GUIDs (same on all Windows locales).
    public static readonly Guid BalancedScheme = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid PowerSaverScheme = Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a");
    public static readonly Guid HighPerformanceScheme = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    // Processor Power Management subgroup GUID.
    private static readonly Guid ProcessorSubgroup = Guid.Parse("54533251-82be-4824-96c1-47b60b740d00");

    // Individual setting GUIDs within the subgroup.
    private static readonly Guid MinProcessorStateGuid = Guid.Parse("893dee8e-2bef-41e0-89c6-b55d0929964c");
    private static readonly Guid MaxProcessorStateGuid = Guid.Parse("bc5038f7-23e0-4960-96da-33abaf5935ec");
    private static readonly Guid ProcessorBoostModeGuid = Guid.Parse("be337238-0d82-4146-a960-4f3749d470c7");

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

    // ── CPU power management settings ─────────────────────────────────────────

    /// <summary>Reads the "Processor performance minimum state" % from the active power scheme.</summary>
    public int? TryGetCpuMinFrequencyPercent()
    {
        if (!TryGetActiveScheme(out Guid scheme)) return null;
        if (!TryReadAcDword(scheme, ProcessorSubgroup, MinProcessorStateGuid, out uint raw)) return null;
        return (int)Math.Clamp(raw, 0u, 100u);
    }

    /// <summary>Reads the "Processor performance maximum state" % from the active power scheme.</summary>
    public int? TryGetCpuMaxFrequencyPercent()
    {
        if (!TryGetActiveScheme(out Guid scheme)) return null;
        if (!TryReadAcDword(scheme, ProcessorSubgroup, MaxProcessorStateGuid, out uint raw)) return null;
        return (int)Math.Clamp(raw, 0u, 100u);
    }

    /// <summary>Reads the "Processor performance boost mode" from the active power scheme.</summary>
    public ProcessorBoostMode? TryGetCpuBoostMode()
    {
        if (!TryGetActiveScheme(out Guid scheme)) return null;
        if (!TryReadAcDword(scheme, ProcessorSubgroup, ProcessorBoostModeGuid, out uint raw)) return null;
        if (!Enum.IsDefined(typeof(ProcessorBoostMode), (int)raw)) return null;
        return (ProcessorBoostMode)(int)raw;
    }

    /// <summary>
    /// Writes the "Processor performance minimum state" % to the active scheme and re-applies it.
    /// Returns false when out of range, when the API fails (e.g. no admin rights), or when the
    /// active scheme cannot be determined.
    /// </summary>
    public bool TrySetCpuMinFrequencyPercent(int percent)
    {
        if (percent < 0 || percent > 100) return false;
        if (!TryGetActiveScheme(out Guid scheme)) return false;
        if (!TryWriteAcDword(scheme, ProcessorSubgroup, MinProcessorStateGuid, (uint)percent)) return false;
        return PowerSetActiveScheme(IntPtr.Zero, ref scheme) == 0;
    }

    /// <summary>
    /// Writes the "Processor performance maximum state" % to the active scheme and re-applies it.
    /// </summary>
    public bool TrySetCpuMaxFrequencyPercent(int percent)
    {
        if (percent < 0 || percent > 100) return false;
        if (!TryGetActiveScheme(out Guid scheme)) return false;
        if (!TryWriteAcDword(scheme, ProcessorSubgroup, MaxProcessorStateGuid, (uint)percent)) return false;
        return PowerSetActiveScheme(IntPtr.Zero, ref scheme) == 0;
    }

    /// <summary>
    /// Writes the "Processor performance boost mode" to the active scheme and re-applies it.
    /// </summary>
    public bool TrySetCpuBoostMode(ProcessorBoostMode mode)
    {
        if (!TryGetActiveScheme(out Guid scheme)) return false;
        if (!TryWriteAcDword(scheme, ProcessorSubgroup, ProcessorBoostModeGuid, (uint)mode)) return false;
        return PowerSetActiveScheme(IntPtr.Zero, ref scheme) == 0;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static bool TryReadAcDword(Guid scheme, Guid subGroup, Guid setting, out uint value)
    {
        value = 0;
        uint type = 0;
        uint bufferSize = 4;
        IntPtr buffer = Marshal.AllocHGlobal(4);
        try
        {
            uint result = PowerReadACValue(
                IntPtr.Zero, ref scheme, ref subGroup, ref setting,
                ref type, buffer, ref bufferSize);
            if (result != 0 || bufferSize != 4) return false;
            value = (uint)Marshal.ReadInt32(buffer);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool TryWriteAcDword(Guid scheme, Guid subGroup, Guid setting, uint value)
    {
        const uint RegDword = 4;
        return PowerWriteACValue(
            IntPtr.Zero, ref scheme, ref subGroup, ref setting,
            RegDword, ref value, sizeof(uint)) == 0;
    }

    // ── P/Invoke declarations ─────────────────────────────────────────────────

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValue(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        ref Guid subGroupGuid,
        ref Guid settingGuid,
        ref uint type,
        IntPtr buffer,
        ref uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValue(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        ref Guid subGroupGuid,
        ref Guid settingGuid,
        uint type,
        ref uint value,
        uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
