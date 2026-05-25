# DynoTune

Workload-aware power, thermal, and noise optimization for AMD Windows PCs.
Thesis/diploma project. C# .NET 8, WinUI 3 (Windows App SDK 1.8), x64/x86/ARM64.

## What This App Does

1 Hz telemetry loop in MainWindow: capture sensor snapshot → classify workload → tick
optimization services → populate LogRecord → push to App.LiveData → UI redraws.
On close: exports telemetry CSV, stability log, and search summary to `%LOCALAPPDATA%\DynoTune\logs\`.

## Solution Structure

```
DynoTune.slnx
DynoTune/              ← C# source project (see DynoTune/CLAUDE.md)
external/              ← AMD ADLX SDK reference; ADLXCSharpBind.dll copied from here at build
```

## Key Architectural Facts

- **App.LiveData**: `MonitoringViewModel` singleton on `App` class; all pages subscribe to its
  `Refreshed` event — never instantiate a second one.
- **Service wiring**: all services are created in `MainWindow` constructor and exposed to pages
  via `App.ConfigureRuntimeServices(...)` static action/func delegates
  (e.g. `App.StartProfileSearchAction`, `App.ForceSafeRollbackAction`).
- **No DI container**: services are newed up directly; pass dependencies via constructors.
- **Namespace**: everything is `DynoTune` or `DynoTune.<Folder>`.
- **Admin note**: F5/Debug runs `asInvoker` — LHM CPU sensors unavailable. For admin runs use the
  unpackaged Release build (`-p:DynoTuneUnpackaged=true -p:UseAdminApplicationManifest=true`) and
  right-click → Run as administrator. Do NOT run the MSIX-mode exe directly (crashes: `REGDB_E_CLASSNOTREG`).
- **Danger/safety**: `DangerState` (level + reason) flows through `App.LiveData` to all pages.
  On `DangerLevel.Critical`, services auto-rollback to safe profile without user confirmation.
- **ADLX write guard**: GPU voltage/clock writes are intentionally restricted to one call path
  (`TryApplyUndervoltCandidate` in `AmdAdlxService`). All other ADLX calls are read-only.

## Hardware Stack

| Layer | Service | Library |
|---|---|---|
| GPU telemetry + tuning | `AmdAdlxService` | AMD ADLX (`ADLXCSharpBind.dll`) |
| CPU / motherboard / fans | `LibreHardwareMonitorService` | LibreHardwareMonitor 0.9.6 |
| CPU sensor fallback | `HwinfoSharedMemoryService` | HWiNFO shared memory (optional) |
| Windows power plans | `WindowsPowerPlanService` | `powrprof.dll` (P/Invoke) |
| Stability events | `StabilityMonitorService` | Windows Event Log (WHEA-Logger, Display/4101) |

## Build

- **Packaged / F5 (default)**: MSIX via `WindowsPackageType=MSIX`. Press F5 in Visual Studio.
- **Unpackaged**: `msbuild -p:DynoTuneUnpackaged=true`. Self-contained, ships WinAppSDK runtime.
- **Admin build**: add `-p:UseAdminApplicationManifest=true` to either mode above.
- ADLX DLL must be present at `external\ADLX\Samples\csharp\x64\Debug\ADLXCSharpBind.dll`.

## After Every Code Change

After making any code changes, always verify a successful build for the target system:

```
& "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\amd64\MSBuild.exe" "C:\Users\Admin\source\repos\DynoTune\DynoTune\DynoTune.csproj" -p:Configuration=Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:DynoTuneUnpackaged=true -p:UseAdminApplicationManifest=true -t:Build -v:minimal
```

Do not report a task complete until this command exits with code 0 (no errors).
If the build fails, fix all errors before finishing.

## Running the App (Release + Admin)

Use the **unpackaged** Release build — it bundles the WinAppSDK runtime so no MSIX install is needed,
and the admin manifest enables full LHM sensor access.

Build output: `DynoTune\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\DynoTune.exe`

Right-click → Run as administrator (or launch from an elevated terminal).

Do NOT run the exe from the default MSIX build output directly — that requires a matching installed
WinAppSDK runtime version and will crash with `REGDB_E_CLASSNOTREG`.

## MVVM Layer Contracts

- `Models/` — plain data classes and enums; no UI or service dependencies.
- `Services/` — hardware access and business logic; no UI references; one service per concern.
- `ViewModels/` — UI state; implement `INotifyPropertyChanged` where needed; no hardware calls.
- `Views/` — XAML + code-behind; subscribe to `App.LiveData.Refreshed`; no service construction.
- Keep `MainWindow` minimal: service wiring + telemetry timer only.
- Never put hardware or optimization logic in XAML code-behind.
