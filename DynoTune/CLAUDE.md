# DynoTune — Source Project

## Entry Points

- `Program.cs` — custom `Main`; sets STA thread; bootstraps WinRT/WinAppSDK; calls `Application.Start`.
  `DisableXamlGeneratedMain` is set in the csproj so this file owns startup.
- `App.xaml.cs` — `App` class; owns `LiveData` (MonitoringViewModel singleton) and all static
  service action delegates set by `ConfigureRuntimeServices`. Also owns `RunStateMarker` lifecycle.
- `MainWindow.xaml.cs` — owns all service instances; runs the 1 Hz `DispatcherTimer` telemetry
  loop (`LoggingTimer_Tick`); calls `App.ConfigureRuntimeServices` at startup.

## Telemetry Tick — `LoggingTimer_Tick` (every 1 second)

1. `MonitoringService.GetCurrentSnapshot()` → `SensorSnapshot`
2. `WorkloadClassifier.Classify(snapshot)` → `ClassificationResult`
3. `AdaptiveOptimizationService.Tick(snapshot, classification)` — may recommend or auto-apply a profile
4. `ProfileSearchService.Tick(snapshot, classification)` — Ch5 multi-phase search loop
5. Build `LogRecord` with all telemetry + optimizer + search state fields
6. `LoggingService.AddRecord(record)`
7. `App.LiveData.Update(snapshot, classification, powerPlanLabel)` → fires `Refreshed` → pages redraw
8. Every 60 ticks: `RefreshStability()` — queries WHEA + GPU reset event counts
9. Every 10 ticks: `EvaluateDangerRollingWindow()` — triggers auto-rollback if thresholds breached

## Workload Classification

`WorkloadClassifier.Classify(SensorSnapshot)` is stateless, single-sample, rule-based.
- Fine output → `WorkloadType`: `Idle, Browsing, Office, Media, Gaming, HeavyCompute, Unknown`
- Coarse output → `WorkloadClass`: `Idle, CpuHeavy, GpuHeavy, Mixed`
Both are on `ClassificationResult` along with a `Reason` string for UI display.

## Danger System

`DangerState` carries `Level` (`Safe / Warning / Critical`), `Reason`, `ReasonDetail`, timestamps,
`AutoRollbackApplied` flag.

Triggers that raise danger:
- WHEA-Logger events (IDs 17, 18, 19, 46)
- GPU driver reset events (Display / event ID 4101)
- Previous unclean shutdown (`RunStateMarker` not written on last close)

On `Critical`: both `ProfileSearchService` and `AdaptiveOptimizationService` stop immediately and
call `RollbackAndStop` / rollback. Auto-rollback applies `ProfileService.GetSafeFallbackProfile()`
power plan without user confirmation.

## Session State File

`%LOCALAPPDATA%\DynoTune\state\last-run.json` — `RunStateMarker` written on clean shutdown,
deleted on next clean start. An unclean prior exit (file still present at launch) raises
`DangerLevel.Warning` with reason `AppCrashDetected` in the next session's danger state.

## Logging / CSV Export

`LoggingService` accumulates `LogRecord` objects in memory. On close (`MainWindow.OnClosed`):
- `telemetry-<timestamp>.csv` — all `LogRecord` rows
- `stability-<timestamp>.txt` — event counts and WHEA details
- `search-summary-<timestamp>.txt` — search session summary

All written to `%LOCALAPPDATA%\DynoTune\logs\`.

## Navigation

`MainWindow` hosts a `NavigationView` + `ContentFrame`.
Page type lookup is in `NavView_SelectionChanged` — a switch on tag string → `typeof(FooPage)`.
Pages: MonitoringPage, TuningPage, ProfilesPage, LogsPage, SettingsPage, DashboardPage.
