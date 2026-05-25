# Services

All services instantiated once in `MainWindow`; no DI container. Telemetry timer fires on UI
thread — services are not thread-safe beyond the UI dispatcher.

## Service Inventory

| Service | Role |
|---|---|
| `MonitoringService` | Aggregates CPU (LHM) + GPU (ADLX) + HWiNFO fallback into `SensorSnapshot` |
| `AmdAdlxService` | AMD GPU telemetry via ADLX SDK; owns an LHM `Computer` (GPU-only scope) |
| `LibreHardwareMonitorService` | CPU temp/clock/power/fans via LHM; most sensors require admin |
| `HwinfoSharedMemoryService` | Optional CPU fallback from HWiNFO shared memory (`Global\HWiNFO_SENS_SM2`) |
| `WorkloadClassifier` | Single-sample rule-based classifier; no state; returns `ClassificationResult` |
| `ProfileService` | In-memory `TuningProfile` list; applies Windows power plan; safe baseline tracking |
| `WindowsPowerPlanService` | P/Invoke into `powrprof.dll`; reads and sets active scheme by GUID |
| `LoggingService` | Accumulates `LogRecord` in memory; exports CSV + logs on close |
| `AdaptiveOptimizationService` | Recommendation-mode optimizer; 20 s cooldown; optional auto-apply |
| `ProfileSearchService` | Ch5 multi-phase voltage/clock search algorithm — see below |
| `StabilityMonitorService` | Wraps WheaMonitorService + GpuResetMonitorService; queried every 60 ticks |
| `WheaMonitorService` | Reads Windows System event log for WHEA-Logger events (IDs 17, 18, 19, 46) |
| `GpuResetMonitorService` | Reads Windows System event log for Display / event ID 4101 |

## ProfileSearchService — Ch5 Algorithm

Phases (in order): `CapturingBaseline → ApplyingCandidate → CapturingTrial → [next candidate] → Completed / RolledBack`

**Sampling windows**: baseline = 25 samples, trial = 15 samples, stabilization holdoff = 5 ticks
before each trial capture begins.

**Loss function** (computed per trial, comparing trial average to baseline average):
```
L_core_e = max(0, uRef - uMeas)          // energy loss: perf below reference
L_core_p = max(0, uMeas - uRef)          // perf loss: perf above reference (overshooting)
L_core   = alpha_c * L_core_e + (1 - alpha_c) * L_core_p     // alpha_c = 0.15
L_mem    = alpha_m * L_mem_e  + (1 - alpha_m) * L_mem_p      // alpha_m = 0.15
L_total  = phi * L_core + (1 - phi) * L_mem                  // phi = 0.33
```

**Weight update** (multiplicative, per candidate k):
```
W_k(t+1) = W_k(t) * (1 - (1 - beta) * L_total_k)    // beta = 0.2
```
After update, normalize: `W_k /= sum(all W_r)`. Floor = 1e-6.
Next candidate = `argmax(weight)` among candidates not yet tried or within cooldown.

**J_k objective score** (lower = better; computed only for accepted candidates):
```
J_k = lambda_p * powerDelta%      // lambda_p = 1.0
    + lambda_n * fanNoiseDelta%   // lambda_n = 0.5
    + lambda_t * max(0, tempDelta)// lambda_t = 0.3
    + lambda_q * perfDrop%        // lambda_q = 2.0  (penalty)
    + lambda_r * riskPenalty      // lambda_r = 1.5
```

**Acceptance gate**: candidate is accepted only if `perfDrop% <= 5.0%` (PerfFloorPercent) AND
no hard safety limit is violated AND danger state is not Critical.

**Voltage safety margin**: `V_apply = V_candidate + GpuSafetyMarginMv` (base = 10 mV).
Workload-specific addend: HeavyCompute +15 mV, Gaming +10 mV, Idle −5 mV.

**Risk penalty** increments:
- +1.0 if `V_apply` is within 10 mV of `VFirstFailMv`
- +0.5 if performance volatility across trial samples exceeds 20%

**Voltage boundary tracking** (per session):
- `VLastKnownGoodMv` / `VFirstFailMv` narrow search range across candidates.
- Do not re-test below `VFirstFailMv` unless environment changed.
- Updated and logged in `SearchEvaluation.VoltageBoundaryUpdate`.

**CPU-only fallback**: if ADLX write fails, trial continues with only the Windows power plan
applied. `LastCpuOnlyFallbackUsed` and `CpuOnlyFallbackCount` are logged in `LogRecord`.

**Anti-oscillation**: 20-tick cooldown (`CooldownTicks`) enforced between candidate switches.

**On critical danger**: records fail boundary, calls `RollbackAndStop()` — idempotent, logged.

## AmdAdlxService — Write Guard

`TryApplyUndervoltCandidate(vApplyMv, maxClockMHz, powerLimitPercent, out string reason)` is the
**only** write path to GPU hardware. All other calls on `AmdAdlxService` are read-only telemetry.
`Initialize()` must return `true` before any read or write. Check `IsInitialized` before calling.

## ProfileService — Baseline

`CaptureVendorSafeBaseline(planKind, gpuMetrics)` is called once at startup with the active power
plan and current GPU readings. This defines the rollback target.
`GetVendorSafeBaselineProfile()` returns that baseline profile.
`GetSafeFallbackProfile()` always returns the "Vendor Safe Baseline" profile (seeded in ctor).
Apply order: `TryApplyPowerPlan` first; GPU apply via `AmdAdlxService` second (optional, guarded).
