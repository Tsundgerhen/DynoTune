# Views

WinUI 3 XAML pages. All live data comes from `App.LiveData` (MonitoringViewModel singleton).
No hardware calls, no service construction, no business logic in code-behind.

## XAML / Code-Behind Pairing Rules

Every page is two files with exactly matching names:
- `FooPage.xaml` — markup; must have `x:Class="DynoTune.Views.FooPage"`
- `FooPage.xaml.cs` — must declare `public sealed partial class FooPage : Page`

`InitializeComponent()` must be called first in the constructor. The triple — XAML filename,
`x:Class`, and code-behind class name — must match exactly or the project will not compile.

## Page Inventory

| Page | Purpose | Live data source |
|---|---|---|
| `MonitoringPage` | Real-time CPU/GPU/fan/stability display with 60-sample sparklines | `App.LiveData.Refreshed` |
| `TuningPage` | Optimizer + search phase controls; danger state display | `App.LiveData.Refreshed` + `TuningPageViewModel` |
| `ProfilesPage` | Profile list, workload targeting, manual apply | `ProfilesPageViewModel` |
| `LogsPage` | In-session log record browser with filters | `LogsPageViewModel` |
| `SettingsPage` | App configuration | (minimal) |
| `DashboardPage` | Overview stub | — |

## LiveData Event Pattern

Subscribe on `Loaded`, unsubscribe on `Unloaded` — always both, to prevent memory leaks:

```csharp
private void Page_Loaded(object sender, RoutedEventArgs e)
{
    App.LiveData.Refreshed += OnRefreshed;
    OnRefreshed(this, EventArgs.Empty);   // draw immediately on first load
}

private void Page_Unloaded(object sender, RoutedEventArgs e)
{
    App.LiveData.Refreshed -= OnRefreshed;
}
```

Pages that use their own ViewModel (e.g. `TuningPage` with `TuningPageViewModel`) instantiate it
in the code-behind constructor and set `DataContext = _vm`.

## MonitoringPage Specifics

- Sparklines are drawn manually via `Canvas` + `Polyline` — **not** data-bound to a collection.
  History is stored in `Queue<double>` fields capped at 60, updated in `OnRefreshed`.
- `_lastFanCount` guards fan row rebuilds — only clears and re-creates `FansPanel` children
  when the number of fans changes. Do not rebuild every tick.
- Sensor provenance strings (`CpuTemperatureSource`, etc.) from `MonitoringViewModel` are shown
  when a sensor is unavailable (e.g. "Unavailable (no admin)").

## TuningPage Specifics

- Danger texts (`DangerLevelText`, `DangerReasonText`, etc.) are set in `RefreshDangerTexts()`
  called on every `LiveData.Refreshed` tick. They read directly from `App.LiveData`, not the VM.
- Control buttons invoke static delegates: `App.StartProfileSearchAction?.Invoke()`,
  `App.ForceSafeRollbackAction?.Invoke()`, etc. **Always null-check** before invoking —
  delegates are set only after `MainWindow` calls `App.ConfigureRuntimeServices`.
- Trial state fields (`TrialBaselinePowerPlan`, candidate info, etc.) come from
  `TuningPageViewModel.SyncRuntimeState()` which reads `App.ProfileSearchService?.State`
  and `App.OptimizationService?.SessionState`.
