using System.Globalization;
using System.Text;
using DynoTune.Models;

namespace DynoTune.Services;

public class LoggingService
{
    private readonly List<LogRecord> _records = new();
    private readonly MonitoringService? _monitoringService;

    public LoggingService()
    {
    }

    public LoggingService(MonitoringService monitoringService)
    {
        _monitoringService = monitoringService;
    }

    public void AddRecord(LogRecord record)
    {
        _records.Add(record);
    }

    public LogRecord CreateRecordFromSnapshot(SensorSnapshot snapshot, string activeProfile)
    {
        return CreateRecordFromSnapshot(snapshot, activeProfile, classification: null);
    }

    public LogRecord CreateRecordFromSnapshot(SensorSnapshot snapshot, string activeProfile, ClassificationResult? classification)
    {
        var record = new LogRecord
        {
            Timestamp = snapshot.Timestamp,
            ActiveProfile = string.IsNullOrWhiteSpace(activeProfile) ? "Default" : activeProfile,
            CpuUsagePercent = snapshot.Cpu.UsagePercent,
            CpuTemperatureC = snapshot.Cpu.TemperatureC,
            CpuClockMHz = snapshot.Cpu.ClockMHz,
            CpuPowerW = snapshot.Cpu.PowerW,
            GpuName = snapshot.Gpu.Name,
            GpuUsagePercent = snapshot.Gpu.UsagePercent,
            GpuTemperatureC = snapshot.Gpu.TemperatureC,
            GpuCoreClockMHz = snapshot.Gpu.CoreClockMHz,
            GpuMemoryClockMHz = snapshot.Gpu.MemoryClockMHz,
            GpuPowerW = snapshot.Gpu.PowerW,
            GpuFanRpm = snapshot.Gpu.FanRpm,
            GpuVramUsageMb = snapshot.Gpu.VramUsageMb,
            MemoryUsedGB = snapshot.MemoryUsedGB,
            MemoryTotalGB = snapshot.MemoryTotalGB,
            SystemPowerW = snapshot.SystemPowerW,
            AmbientTemperatureC = snapshot.AmbientTemperatureC
        };

        if (classification is not null)
        {
            record.WorkloadType = classification.WorkloadType;
            record.CoarseWorkloadClass = classification.CoarseWorkloadClass;
            record.ClassificationReason = classification.Reason;
        }

        return record;
    }

    public LogRecord CaptureCurrentRecord(string activeProfile)
    {
        if (_monitoringService is null)
        {
            throw new InvalidOperationException("MonitoringService is not configured.");
        }

        SensorSnapshot snapshot = _monitoringService.GetCurrentSnapshot();
        LogRecord record = CreateRecordFromSnapshot(snapshot, activeProfile);
        AddRecord(record);
        return record;
    }

    public IReadOnlyList<LogRecord> GetRecords()
    {
        return _records;
    }

    public async Task SaveToCsvAsync(string filePath)
    {
        StringBuilder csv = new();
        csv.AppendLine("Timestamp,ActiveProfile,WorkloadType,CoarseWorkloadClass,ClassificationReason,CpuUsagePercent,CpuTemperatureC,CpuClockMHz,CpuPowerW,GpuName,GpuUsagePercent,GpuTemperatureC,GpuCoreClockMHz,GpuMemoryClockMHz,GpuPowerW,GpuFanRpm,GpuVramUsageMb,MemoryUsedGB,MemoryTotalGB,SystemPowerW,AmbientTemperatureC");

        foreach (LogRecord record in _records)
        {
            csv.Append(record.Timestamp.ToString("O", CultureInfo.InvariantCulture)).Append(',');
            csv.Append(EscapeCsv(record.ActiveProfile)).Append(',');
            csv.Append(EscapeCsv(record.WorkloadType.ToString())).Append(',');
            csv.Append(EscapeCsv(record.CoarseWorkloadClass.ToString())).Append(',');
            csv.Append(EscapeCsv(record.ClassificationReason)).Append(',');
            csv.Append(record.CpuUsagePercent.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(record.CpuTemperatureC?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(record.CpuClockMHz.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(record.CpuPowerW?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(EscapeCsv(record.GpuName)).Append(',');
            csv.Append(record.GpuUsagePercent.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(record.GpuTemperatureC.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(record.GpuCoreClockMHz.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(record.GpuMemoryClockMHz.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(record.GpuPowerW.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(record.GpuFanRpm.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(record.GpuVramUsageMb?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(record.MemoryUsedGB.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(record.MemoryTotalGB.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(record.SystemPowerW?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(record.AmbientTemperatureC?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            csv.AppendLine();
        }

        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(filePath, csv.ToString(), Encoding.UTF8);
    }

    // Backward-compatible helper for old call sites.
    public LogRecord AddSnapshot(SensorSnapshot snapshot)
    {
        LogRecord record = CreateRecordFromSnapshot(snapshot, "Default");
        AddRecord(record);
        return record;
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return value;
    }
}
