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
        csv.AppendLine("Timestamp,ActiveProfile,WorkloadType,CoarseWorkloadClass,ClassificationReason,CpuUsagePercent,CpuTemperatureC,CpuClockMHz,CpuPowerW,GpuName,GpuUsagePercent,GpuTemperatureC,GpuCoreClockMHz,GpuMemoryClockMHz,GpuPowerW,GpuFanRpm,GpuVramUsageMb,MemoryUsedGB,MemoryTotalGB,SystemPowerW,AmbientTemperatureC,DangerLevel,DangerReason,DangerReasonDetail,DangerRollbackApplied,OptimizerPhase,OptimizerCandidateName,OptimizerCandidateApplied,OptimizerAcceptanceReason,OptimizerRollbackReason,SearchSessionId,SearchWorkloadType,SearchPhase,SearchCandidateId,SearchCandidateIndex,SearchAccepted,SearchDecision,SearchPerfDropPercent,SearchPowerDeltaPercent,SearchRequestedGpuVoltageMv,SearchAppliedGpuVoltageMv,SearchGpuSafetyMarginMv,SearchRequestedGpuClockMHz,SearchRequestedGpuPowerLimitPercent,SearchRequestedPowerPlan,SearchConfirmedPowerPlan,SearchPowerPlanConfirmed,SearchCandidateWeight,SearchLossEnergy,SearchLossPerf,SearchLossTotal,SearchWeightAfterUpdate,SearchObjectiveScore,SearchRiskPenalty,SearchVolatility,SearchVoltageBoundaryUpdate,SearchVLastKnownGoodMv,SearchVFirstFailMv,SearchBaselineAvgPowerW,SearchBaselineAvgPerfProxy,SearchBaselineAvgFanRpm,SearchGpuApplySucceeded,SearchCpuOnlyFallbackUsed,SearchApplyFailureReason,SearchCpuOnlyFallbackCount,SearchCandidateDecision,SearchNextAction");

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
            csv.Append(record.AmbientTemperatureC?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(EscapeCsv(record.DangerLevel.ToString())).Append(',');
            csv.Append(EscapeCsv(record.DangerReason.ToString())).Append(',');
            csv.Append(EscapeCsv(record.DangerReasonDetail)).Append(',');
            csv.Append(record.DangerRollbackApplied ? "1" : "0").Append(',');
            csv.Append(EscapeCsv(record.OptimizerPhase)).Append(',');
            csv.Append(EscapeCsv(record.OptimizerCandidateName)).Append(',');
            csv.Append(record.OptimizerCandidateApplied ? "1" : "0").Append(',');
            csv.Append(EscapeCsv(record.OptimizerAcceptanceReason)).Append(',');
            csv.Append(EscapeCsv(record.OptimizerRollbackReason)).Append(',');
            csv.Append(EscapeCsv(record.SearchSessionId)).Append(',');
            csv.Append(EscapeCsv(record.SearchWorkloadType.ToString())).Append(',');
            csv.Append(EscapeCsv(record.SearchPhase)).Append(',');
            csv.Append(EscapeCsv(record.SearchCandidateId)).Append(',');
            csv.Append(record.SearchCandidateIndex.ToString(CultureInfo.InvariantCulture)).Append(',');
            csv.Append(record.SearchAccepted ? "1" : "0").Append(',');
            csv.Append(EscapeCsv(record.SearchDecision)).Append(',');
            csv.Append(record.SearchPerfDropPercent?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(record.SearchPowerDeltaPercent?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(record.SearchRequestedGpuVoltageMv?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(record.SearchAppliedGpuVoltageMv?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(record.SearchGpuSafetyMarginMv?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(record.SearchRequestedGpuClockMHz?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(record.SearchRequestedGpuPowerLimitPercent?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(EscapeCsv(record.SearchRequestedPowerPlan)).Append(',');
            csv.Append(EscapeCsv(record.SearchConfirmedPowerPlan)).Append(',');
            csv.Append(record.SearchPowerPlanConfirmed.HasValue
                ? (record.SearchPowerPlanConfirmed.Value ? "1" : "0")
                : string.Empty).Append(',');
            csv.Append(record.SearchCandidateWeight?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(record.SearchLossEnergy?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(record.SearchLossPerf?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(record.SearchLossTotal?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(record.SearchWeightAfterUpdate?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(record.SearchObjectiveScore?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(record.SearchRiskPenalty?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(record.SearchVolatility?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(EscapeCsv(record.SearchVoltageBoundaryUpdate)).Append(',');
            csv.Append(record.SearchVLastKnownGoodMv?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(record.SearchVFirstFailMv?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(record.SearchBaselineAvgPowerW?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(record.SearchBaselineAvgPerfProxy?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(record.SearchBaselineAvgFanRpm?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(record.SearchGpuApplySucceeded.HasValue
                ? (record.SearchGpuApplySucceeded.Value ? "1" : "0")
                : string.Empty).Append(',');
            csv.Append(record.SearchCpuOnlyFallbackUsed ? "1" : "0").Append(',');
            csv.Append(EscapeCsv(record.SearchApplyFailureReason)).Append(',');
            csv.Append(record.SearchCpuOnlyFallbackCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',');
            csv.Append(EscapeCsv(record.SearchCandidateDecision)).Append(',');
            csv.Append(EscapeCsv(record.SearchNextAction));
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
