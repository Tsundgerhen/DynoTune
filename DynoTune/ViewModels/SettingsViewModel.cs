using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using DynoTune.Models;
using DynoTune.Services;

namespace DynoTune.ViewModels;

[SupportedOSPlatform("windows10.0.19041.0")]
public class SettingsViewModel : INotifyPropertyChanged
{
    private double _maxCpuTempC;
    private double _maxGpuTempC;
    private double _maxGpuHotspotTempC;
    private double _maxPerfLossPercent;
    private double _maxSearchCandidates;
    private double _samplingIntervalMs;
    private double _shortTestDurationSec;
    private double _longTestDurationSec;
    private string _selectedTargetMode = "Balanced";
    private string _statusText = string.Empty;

    public IReadOnlyList<string> TargetModeOptions { get; } = ["Performance", "Balanced", "Energy Saving"];

    public double MaxCpuTempC
    {
        get => _maxCpuTempC;
        set { _maxCpuTempC = value; OnPropertyChanged(); }
    }

    public double MaxGpuTempC
    {
        get => _maxGpuTempC;
        set { _maxGpuTempC = value; OnPropertyChanged(); }
    }

    public double MaxGpuHotspotTempC
    {
        get => _maxGpuHotspotTempC;
        set { _maxGpuHotspotTempC = value; OnPropertyChanged(); }
    }

    public double MaxPerfLossPercent
    {
        get => _maxPerfLossPercent;
        set { _maxPerfLossPercent = value; OnPropertyChanged(); }
    }

    public double MaxSearchCandidates
    {
        get => _maxSearchCandidates;
        set { _maxSearchCandidates = value; OnPropertyChanged(); }
    }

    public double SamplingIntervalMs
    {
        get => _samplingIntervalMs;
        set { _samplingIntervalMs = value; OnPropertyChanged(); }
    }

    public double ShortTestDurationSec
    {
        get => _shortTestDurationSec;
        set { _shortTestDurationSec = value; OnPropertyChanged(); }
    }

    public double LongTestDurationSec
    {
        get => _longTestDurationSec;
        set { _longTestDurationSec = value; OnPropertyChanged(); }
    }

    public string SelectedTargetMode
    {
        get => _selectedTargetMode;
        set { _selectedTargetMode = value ?? "Balanced"; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    public void Load()
    {
        AppSettings s = App.SettingsService?.Current ?? new AppSettings();
        _maxCpuTempC = s.MaxCpuTemperatureC;
        _maxGpuTempC = s.MaxGpuTemperatureC;
        _maxGpuHotspotTempC = s.MaxGpuHotspotTemperatureC;
        _maxPerfLossPercent = s.MaxPerfLossPercent;
        _maxSearchCandidates = s.MaxSearchCandidates;
        _samplingIntervalMs = s.SamplingIntervalMs;
        _shortTestDurationSec = s.ShortTestDurationSec;
        _longTestDurationSec = s.LongTestDurationSec;
        _selectedTargetMode = s.TargetMode switch
        {
            OptimizationTargetMode.Performance => "Performance",
            OptimizationTargetMode.EnergySaving => "Energy Saving",
            _ => "Balanced"
        };
        OnPropertyChanged(string.Empty);
        StatusText = string.Empty;
    }

    public void ApplyAndSave()
    {
        AppSettingsService? svc = App.SettingsService;
        if (svc is null)
        {
            StatusText = "Settings service unavailable.";
            return;
        }

        AppSettings s = svc.Current;
        s.MaxCpuTemperatureC = _maxCpuTempC;
        s.MaxGpuTemperatureC = _maxGpuTempC;
        s.MaxGpuHotspotTemperatureC = _maxGpuHotspotTempC;
        s.MaxPerfLossPercent = _maxPerfLossPercent;
        s.MaxSearchCandidates = (int)Math.Round(_maxSearchCandidates);
        s.SamplingIntervalMs = (int)Math.Round(_samplingIntervalMs);
        s.ShortTestDurationSec = (int)Math.Round(_shortTestDurationSec);
        s.LongTestDurationSec = (int)Math.Round(_longTestDurationSec);
        s.TargetMode = _selectedTargetMode switch
        {
            "Performance" => OptimizationTargetMode.Performance,
            "Energy Saving" => OptimizationTargetMode.EnergySaving,
            _ => OptimizationTargetMode.Balanced
        };

        svc.Save();
        App.ApplySettingsAction?.Invoke();
        StatusText = "Settings saved. Search parameters take effect on next search start.";
    }

    public void ResetToDefaults()
    {
        var defaults = new AppSettings();
        _maxCpuTempC = defaults.MaxCpuTemperatureC;
        _maxGpuTempC = defaults.MaxGpuTemperatureC;
        _maxGpuHotspotTempC = defaults.MaxGpuHotspotTemperatureC;
        _maxPerfLossPercent = defaults.MaxPerfLossPercent;
        _maxSearchCandidates = defaults.MaxSearchCandidates;
        _samplingIntervalMs = defaults.SamplingIntervalMs;
        _shortTestDurationSec = defaults.ShortTestDurationSec;
        _longTestDurationSec = defaults.LongTestDurationSec;
        _selectedTargetMode = "Balanced";
        OnPropertyChanged(string.Empty);
        StatusText = "Reset to defaults. Press Apply to save.";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
