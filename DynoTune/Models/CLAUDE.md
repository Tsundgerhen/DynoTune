# Models

Plain C# data classes and enums. No UI references, no service dependencies, no logic beyond
simple property defaults. Every model lives in the `DynoTune.Models` namespace.

## Core Telemetry Types

| Model | Role |
|---|---|
| `SensorSnapshot` | One complete sample: `CpuMetrics`, `GpuMetrics`, fan list, memory GB, system power |
| `CpuMetrics` | Usage%, temp, clock, power, fan RPM — nullable except usage; source label strings |
| `GpuMetrics` | Usage%, core temp, hotspot temp, core/mem clocks, voltage mV, power W, fan RPM/%, VRAM |
| `FanInfo` | Name, RPM (int?), percent (double?) for one fan sensor |
| `ClassificationResult` | `WorkloadType` (fine) + `WorkloadClass` (coarse) + `Reason` string |
| `TuningProfile` | Named profile: `PreferredPowerPlan`, GPU (voltage mV, clock MHz, power limit %), CPU (PPT/TDC/EDC W/A), fan curve, `SafetyLimits`, `IsVendorGenerated` flag |
| `SafetyLimits` | Thermal ceilings per profile (defaults: CPU 90°C, GPU 85°C, hotspot 100°C) |
| `DangerState` | Live danger: `DangerLevel`, `DangerReason`, detail string, timestamps, `AutoRollbackApplied` |

## Search / Optimizer Types

| Model | Role |
|---|---|
| `SearchCandidate` | One trial point: power plan + optional GPU params + `Weight` + `GpuSafetyMarginMv` |
| `SearchEvaluation` | Trial result: accepted flag, rejection reason, power/perf deltas, loss breakdown (`LossEnergy`, `LossPerf`, `LossTotal`), `WeightAfterUpdate`, `ObjectiveScore` (J_k), `VoltageBoundaryUpdate` |
| `WorkloadSearchState` | All mutable search session state owned by `ProfileSearchService`; exposed as `State` property |
| `OptimizationSessionState` | Optimizer session state owned by `AdaptiveOptimizationService`; exposed as `SessionState` |
| `OptimizationCandidate` | Simpler candidate used by `AdaptiveOptimizationService` (power plan + GPU params) |
| `StabilitySnapshot` | WHEA event counts, GPU driver reset counts, detailed event records |

## Key Enums

- `WorkloadType`: `Unknown, Idle, Browsing, Office, Media, Gaming, HeavyCompute`
- `WorkloadClass`: `Idle, CpuHeavy, GpuHeavy, Mixed`
- `WindowsPowerPlanKind`: `Balanced, PowerSaver, HighPerformance`
- `DangerLevel`: `Safe, Warning, Critical`
- `DangerReason`: `None, WheaEvent, GpuDriverReset, AppCrashDetected, ManualRollback`
- `WorkloadSearchPhase`: `Idle, CapturingBaseline, ApplyingCandidate, CapturingTrial, Completed, RolledBack`
- `OptimizationPhase`: phase states for `AdaptiveOptimizationService`

## LogRecord — CSV Row (~80 fields)

`LogRecord` is one row in the exported CSV. Field groups:

| Group | Fields |
|---|---|
| Session / profile | Timestamp, SessionId, ActiveProfileName, PowerPlanLabel |
| Workload | WorkloadType, WorkloadClass, WorkloadReason |
| CPU metrics | CpuUsage, CpuTemperature, CpuClockMHz, CpuPowerW (all double?) |
| GPU metrics | GpuUsage, GpuTemperature, GpuHotspotTemp, GpuCoreClockMHz, GpuMemClockMHz, GpuVoltageMv, GpuPowerW, GpuFanRpm (all double?) |
| Memory / system | MemoryUsedGb, SystemPowerW |
| Danger state | DangerLevel, DangerReason, DangerDetail, AutoRollbackApplied |
| Optimizer state | OptPhase, RecommendedProfileName, AutoApplyEnabled, AutoApplyAcceptCount |
| Search state (30+ fields) | SearchPhase, CandidateId, CandidateIndex, BaselineVoltage, TrialVoltage, SafetyMargin, PerfDrop, PowerDelta, LossEnergy, LossPerf, LossTotal, ObjectiveScore, WeightBefore, WeightAfter, VLastKnownGood, VFirstFail, Accepted, RejectionReason, CpuOnlyFallback, RiskPenalty, VolatilityPct, ... |

**Adding a new telemetry field to LogRecord:**
1. Add the property here with a sensible default (`string.Empty` or `null`, not `0` for optional numerics).
2. Populate it in `MainWindow.LoggingTimer_Tick` after the relevant service call.
3. Add the CSV header string and value to `LoggingService.SaveToCsvAsync`.

## Nullable Conventions

Use `double?` / `int?` for optional hardware readings so missing sensors are distinguishable from
zero in analysis. Search-specific fields that are irrelevant outside a search session should also
be nullable — avoids spurious zeros in non-search CSV rows and keeps thesis data clean.
