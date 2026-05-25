using System.IO.MemoryMappedFiles;
using System.Threading;
using DynoTune.Models;

namespace DynoTune.Services;

/// <summary>
/// Optional HWiNFO shared-memory reader used as fallback when LHM sensors are missing.
/// Requires HWiNFO running with Shared Memory enabled.
/// </summary>
public sealed class HwinfoSharedMemoryService
{
    private const string SharedMemoryPath = @"Global\HWiNFO_SENS_SM2";
    private const string SharedMemoryMutex = @"Global\HWiNFO_SM2_MUTEX";
    private const uint SignatureActive = 0x53695748; // "HWiS"

    private const int ReadingTypeOffset = 0;
    private const int ReadingLabelOrigOffset = 12;
    private const int ReadingLabelUserOffset = 140;
    private const int ReadingUnitOffset = 268;
    private const int ReadingValueOffset = 284;
    private const int ReadingLabelLength = 128;
    private const int ReadingUnitLength = 16;

    private bool _loggedNoSharedMemory;
    private bool _loggedConnected;

    public bool TryGetCpuTelemetry(out HwinfoCpuTelemetry telemetry)
    {
        telemetry = new HwinfoCpuTelemetry();

        try
        {
            using var mapped = MemoryMappedFile.OpenExisting(SharedMemoryPath, MemoryMappedFileRights.Read);
            using var view = mapped.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

            if (!TryReadHeader(view, out SharedHeader header))
            {
                return false;
            }

            using Mutex? mutex = TryOpenMutex();
            bool lockTaken = false;
            try
            {
                if (mutex != null)
                {
                    lockTaken = mutex.WaitOne(10);
                }

                telemetry = ReadCpuTelemetry(view, header);
            }
            finally
            {
                if (lockTaken && mutex != null)
                {
                    mutex.ReleaseMutex();
                }
            }

            bool hasAny =
                telemetry.HasTemperature ||
                telemetry.HasClock ||
                telemetry.HasPower ||
                telemetry.HasFan;

            if (hasAny && !_loggedConnected)
            {
                _loggedConnected = true;
                System.Diagnostics.Debug.WriteLine("[HWiNFO] Shared memory connected; CPU fallback data available.");
            }

            return hasAny;
        }
        catch
        {
            if (!_loggedNoSharedMemory)
            {
                _loggedNoSharedMemory = true;
                System.Diagnostics.Debug.WriteLine("[HWiNFO] Shared memory not available. Run HWiNFO Sensors with Shared Memory enabled.");
            }

            return false;
        }
    }

    private static HwinfoCpuTelemetry ReadCpuTelemetry(MemoryMappedViewAccessor view, SharedHeader header)
    {
        double? bestTemp = null;
        double? bestClock = null;
        double? bestPower = null;
        double? bestFan = null;
        double? bestFanPct = null;
        var systemFans = new List<FanInfo>();

        for (uint i = 0; i < header.NumReadingElements; i++)
        {
            long baseOffset = header.OffsetReadingSection + (long)header.SizeReadingElement * i;
            if (baseOffset < 0 || baseOffset + ReadingValueOffset + sizeof(double) > view.Capacity)
            {
                continue;
            }

            uint readingType = unchecked((uint)view.ReadInt32(baseOffset + ReadingTypeOffset));
            string labelUser = ReadNullTerminatedAscii(view, baseOffset + ReadingLabelUserOffset, ReadingLabelLength);
            string labelOrig = ReadNullTerminatedAscii(view, baseOffset + ReadingLabelOrigOffset, ReadingLabelLength);
            string label = string.IsNullOrWhiteSpace(labelUser) ? labelOrig : labelUser;
            if (!LooksLikeCpuLabel(label))
            {
                continue;
            }

            string unit = ReadNullTerminatedAscii(view, baseOffset + ReadingUnitOffset, ReadingUnitLength);
            double value = view.ReadDouble(baseOffset + ReadingValueOffset);
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                continue;
            }

            switch (readingType)
            {
                case 1: // Temperature
                    if (value is >= 10 and <= 115 && PreferTemperatureLabel(label, ref bestTemp))
                    {
                        bestTemp = value;
                    }
                    break;
                case 5: // Power
                    if (unit.Contains("W", StringComparison.OrdinalIgnoreCase) && value > 0)
                    {
                        if (!bestPower.HasValue || label.Contains("Package", StringComparison.OrdinalIgnoreCase))
                            bestPower = value;
                    }
                    break;
                case 6: // Clock
                    if (unit.Contains("MHz", StringComparison.OrdinalIgnoreCase) &&
                        value > 0 &&
                        IsPreferredClockLabel(label))
                    {
                        if (!bestClock.HasValue || value > bestClock.Value)
                        {
                            bestClock = value;
                        }
                    }
                    break;
                case 3: // Fan
                    if (unit.Contains("RPM", StringComparison.OrdinalIgnoreCase) &&
                        value > 0)
                    {
                        if (label.Contains("CPU", StringComparison.OrdinalIgnoreCase))
                        {
                            bestFan = value;
                        }
                        else if (LooksLikeSystemFanLabel(label))
                        {
                            systemFans.Add(new FanInfo
                            {
                                Name = $"{label} (HWiNFO)",
                                Rpm = value
                            });
                        }
                    }
                    break;
                case 7: // Usage
                    // Ignore for now; LHM usage is already available.
                    break;
                case 8: // Other
                    if (label.Contains("CPU Fan", StringComparison.OrdinalIgnoreCase) &&
                        unit.Contains("%", StringComparison.OrdinalIgnoreCase) &&
                        value is >= 0 and <= 100)
                    {
                        bestFanPct = value;
                    }
                    break;
            }
        }

        return new HwinfoCpuTelemetry
        {
            TemperatureC = bestTemp,
            ClockMHz = bestClock,
            PackagePowerW = bestPower,
            CpuFanRpm = bestFan,
            CpuFanPercent = bestFanPct,
            AdditionalSystemFans = systemFans
        };
    }

    private static bool LooksLikeCpuLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        if (label.Contains("GPU", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return label.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
               label.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
               label.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
               label.Contains("Tdie", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPreferredClockLabel(string label)
    {
        return label.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
               label.Contains("Effective Clock", StringComparison.OrdinalIgnoreCase) ||
               label.Contains("CPU Clock", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeSystemFanLabel(string label)
    {
        return label.Contains("System", StringComparison.OrdinalIgnoreCase) ||
               label.Contains("SYS", StringComparison.OrdinalIgnoreCase) ||
               label.Contains("Chassis", StringComparison.OrdinalIgnoreCase) ||
               label.Contains("Case", StringComparison.OrdinalIgnoreCase) ||
               label.Contains("CHA", StringComparison.OrdinalIgnoreCase);
    }

    private static bool PreferTemperatureLabel(string label, ref double? current)
    {
        if (label.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("Tdie", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("Package", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !current.HasValue;
    }

    private static string ReadNullTerminatedAscii(MemoryMappedViewAccessor view, long offset, int maxLength)
    {
        if (offset < 0 || offset + maxLength > view.Capacity)
        {
            return string.Empty;
        }

        byte[] buffer = new byte[maxLength];
        view.ReadArray(offset, buffer, 0, maxLength);

        int end = Array.IndexOf(buffer, (byte)0);
        if (end < 0)
        {
            end = maxLength;
        }

        return System.Text.Encoding.ASCII.GetString(buffer, 0, end).Trim();
    }

    private static Mutex? TryOpenMutex()
    {
        try
        {
            return Mutex.OpenExisting(SharedMemoryMutex);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadHeader(MemoryMappedViewAccessor view, out SharedHeader header)
    {
        header = default;
        if (view.Capacity < 44)
        {
            return false;
        }

        uint signature = unchecked((uint)view.ReadInt32(0));
        if (signature != SignatureActive)
        {
            return false;
        }

        // Header layout (last_update is int64 at offset 12, so reading section starts at 32):
        // 0  magic | 4 version | 8 version2 | 12 last_update(8 bytes)
        // 20 sensor_section_offset | 24 sensor_element_size | 28 sensor_element_count
        // 32 entry_section_offset  | 36 entry_element_size  | 40 entry_element_count
        header = new SharedHeader
        {
            OffsetReadingSection = unchecked((uint)view.ReadInt32(32)),
            SizeReadingElement   = unchecked((uint)view.ReadInt32(36)),
            NumReadingElements   = unchecked((uint)view.ReadInt32(40))
        };

        return header.OffsetReadingSection > 0 &&
               header.SizeReadingElement >= 300 &&
               header.NumReadingElements > 0;
    }

    private struct SharedHeader
    {
        public uint OffsetReadingSection;
        public uint SizeReadingElement;
        public uint NumReadingElements;
    }
}
