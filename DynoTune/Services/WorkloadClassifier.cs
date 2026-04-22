using DynoTune.Models;

namespace DynoTune.Services;

/// <summary>
/// Rule-based workload label from CPU/GPU/VRAM/RAM usage (single-sample, no hysteresis).
/// </summary>
public class WorkloadClassifier
{
    // Thresholds (percent 0–100 unless noted). Tune for your hardware/thesis runs.
    private const double LowCpu = 10.0;
    private const double LowGpu = 8.0;
    private const double HighCpu = 65.0;
    private const double HighGpu = 55.0;
    private const double GamingGpu = 45.0;
    private const double GamingVramMb = 1200.0;
    private const double HeavyComputeGpuCeiling = 25.0;
    private const double MediaGpuMin = 12.0;
    private const double MediaGpuMax = 52.0;
    private const double MediaCpuMax = 48.0;
    private const double RamHeavyOfficeRatio = 0.52;

    public ClassificationResult Classify(SensorSnapshot snapshot)
    {
        double cpu = snapshot.Cpu.UsagePercent;
        double gpu = snapshot.Gpu.UsagePercent;
        double? vramMb = snapshot.Gpu.VramUsageMb;
        double ramRatio = snapshot.MemoryTotalGB > 0.1
            ? snapshot.MemoryUsedGB / snapshot.MemoryTotalGB
            : 0.0;

        // 1) Idle
        if (cpu < LowCpu && gpu < LowGpu)
        {
            return Finish(WorkloadType.Idle, WorkloadClass.Idle, "low CPU and low GPU");
        }

        // 2) Gaming — strong GPU load, VRAM use when available
        bool vramHigh = vramMb.HasValue && vramMb.Value >= GamingVramMb;
        if (gpu >= HighGpu || (gpu >= GamingGpu && vramHigh))
        {
            return Finish(WorkloadType.Gaming, WorkloadClass.GpuHeavy, vramHigh
                ? "high GPU and elevated VRAM"
                : "high GPU utilization");
        }

        // 3) Heavy compute — CPU-bound, GPU mostly idle
        if (cpu >= HighCpu && gpu < HeavyComputeGpuCeiling)
        {
            return Finish(WorkloadType.HeavyCompute, WorkloadClass.CpuHeavy, "high CPU, low GPU");
        }

        // 4) Media — moderate GPU (decode) without gaming-level load
        if (gpu >= MediaGpuMin && gpu < MediaGpuMax && cpu < MediaCpuMax && cpu >= LowCpu)
        {
            return Finish(WorkloadType.Media, WorkloadClass.Mixed, "moderate GPU, moderate CPU (media-style)");
        }

        // 5) Office vs browsing — medium CPU, low GPU
        if (gpu < LowGpu * 2.5 && cpu >= LowCpu && cpu < HighCpu)
        {
            if (ramRatio >= RamHeavyOfficeRatio && cpu >= 18.0)
            {
                return Finish(WorkloadType.Office, WorkloadClass.Mixed, "medium CPU, low GPU, high RAM use");
            }

            if (cpu >= LowCpu)
            {
                return Finish(WorkloadType.Browsing, WorkloadClass.Mixed, "medium CPU, low GPU");
            }
        }

        return Finish(WorkloadType.Unknown, WorkloadClass.Mixed, "no rule matched");
    }

    private static ClassificationResult Finish(WorkloadType type, WorkloadClass coarse, string reason)
    {
        return new ClassificationResult
        {
            WorkloadType = type,
            CoarseWorkloadClass = coarse,
            Reason = reason
        };
    }
}
