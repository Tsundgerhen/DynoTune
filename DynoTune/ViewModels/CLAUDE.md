# ViewModels

UI state classes. No hardware calls, no service construction. Access services only through
the static delegates and service references on `App` (e.g. `App.ProfileService?.Method()`).

## INotifyPropertyChanged Pattern

ViewModels that data-bind to XAML implement `INotifyPropertyChanged`:

```csharp
private string _foo = string.Empty;
public string Foo
{
    get => _foo;
    set { _foo = value ?? string.Empty; OnPropertyChanged(); }
}

public event PropertyChangedEventHandler? PropertyChanged;
private void OnPropertyChanged([CallerMemberName] string? name = null)
    => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
```

Use `[CallerMemberName]` — never pass the property name as a string literal.

## MonitoringViewModel (special — no INPC)

`App.LiveData` is the singleton instance. All properties are `{ get; private set; }`.
`MainWindow` calls `Update(snapshot, classification, powerPlanLabel)` each telemetry tick,
then fires the `Refreshed` event. Pages redraw by subscribing to `Refreshed`.

**Do NOT call `Update`, `UpdateStability`, or `UpdateDanger` from a page** — only `MainWindow`.

Adding a new live property:
1. Add `public T Foo { get; private set; }` here.
2. Set it inside `Update()` (or `UpdateStability()`/`UpdateDanger()` as appropriate).
3. Read it in the page's `OnRefreshed` handler.

## TuningPageViewModel

Instantiated per-page in `TuningPage` code-behind (`_vm = new TuningPageViewModel()`).
- `Refresh()` — called on page `Loaded`; populates `ProfileNames` from `App.ProfileService`.
- `SyncRuntimeState()` — called on every `LiveData.Refreshed` tick; reads optimizer and search
  state from `App.OptimizationService?.SessionState` and `App.ProfileSearchService?.State`,
  copies fields to bindable string/bool properties for XAML binding.

## LogsPageViewModel

`ObservableCollection<LogRecord> Records` — populated by `LoadRecords()` called from
`LogsPage.Loaded`. Filtered by `SearchText`, `SelectedWorkload`, `SelectedDanger`.
`ExportCsvAsync(path)` delegates to `App.LoggingService?.SaveToCsvAsync(path)`.

## ProfilesPageViewModel

Wraps profile list from `App.ProfileService`.
`ApplySelectedProfile()` → `App.ProfileService?.SetActiveProfile(name)` then
`App.ProfileService?.TryApplyPowerPlan(profile)`.
`Profiles` is an `ObservableCollection<TuningProfile>` refreshed on `Refresh()`.

## Adding a New ViewModel

1. Create `FooViewModel.cs` in `ViewModels/`, implement `INotifyPropertyChanged` if data-bound.
2. Instantiate in `FooPage.xaml.cs` constructor; `DataContext = _vm` if using `{Binding}`.
3. If you need live telemetry, subscribe to `App.LiveData.Refreshed` in the **page** and call
   a sync method on the VM from the handler — do not subscribe inside the VM itself.
4. Do not store service instances in the VM — call `App.SomeService?.Method()` directly.
