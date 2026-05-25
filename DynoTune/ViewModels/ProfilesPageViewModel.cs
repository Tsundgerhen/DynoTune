using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Linq;
using DynoTune.Models;
using DynoTune.Services;

namespace DynoTune.ViewModels;

[SupportedOSPlatform("windows10.0.19041.0")]
public class ProfilesPageViewModel : INotifyPropertyChanged
{
    private TuningProfile? _selectedProfile;
    private string _statusText = "Ready";

    public ObservableCollection<TuningProfile> Profiles { get; } = new();
    public IReadOnlyList<WorkloadClass> WorkloadClasses { get; } =
        Enum.GetValues<WorkloadClass>();
    public IReadOnlyList<WindowsPowerPlanKind> PowerPlanOptions { get; } =
        Enum.GetValues<WindowsPowerPlanKind>();
    public IReadOnlyList<ProcessorBoostMode> BoostModeOptions { get; } =
        Enum.GetValues<ProcessorBoostMode>();

    public TuningProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            _selectedProfile = value;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public void Refresh()
    {
        Profiles.Clear();
        ProfileService? svc = App.ProfileService;
        if (svc is null)
        {
            StatusText = "Profile service unavailable.";
            return;
        }

        foreach (TuningProfile p in svc.Profiles)
        {
            Profiles.Add(p);
        }

        SelectedProfile = svc.ActiveProfile ?? Profiles.FirstOrDefault();
        StatusText = $"Loaded {Profiles.Count} profile(s). Active: {svc.ActiveProfile?.Name ?? "(none)"}";
    }

    public bool SetActiveSelected()
    {
        if (SelectedProfile is null || App.ProfileService is null)
        {
            return false;
        }

        bool ok = App.ProfileService.SetActiveProfile(SelectedProfile.Name);
        StatusText = ok ? $"Active profile set: {SelectedProfile.Name}" : "Failed to set active profile.";
        return ok;
    }

    public bool ApplySelected()
    {
        if (SelectedProfile is null || App.ProfileService is null)
        {
            return false;
        }

        bool planOk = App.ProfileService.TryApplyPowerPlan(SelectedProfile);
        bool cpuOk = ApplyCpuPowerSettings(SelectedProfile);

        StatusText = planOk && cpuOk
            ? $"Applied: {SelectedProfile.PreferredPowerPlan}"
            : planOk
                ? "Power plan applied; CPU settings failed (admin required?)"
                : "Failed to apply selected profile power plan.";
        return planOk && cpuOk;
    }

    private static bool ApplyCpuPowerSettings(TuningProfile profile)
    {
        WindowsPowerPlanService? svc = App.PowerPlanService;
        if (svc is null) return true;

        bool ok = true;
        if (profile.CpuMinFrequencyPercent.HasValue)
            ok &= svc.TrySetCpuMinFrequencyPercent(profile.CpuMinFrequencyPercent.Value);
        if (profile.CpuMaxFrequencyPercent.HasValue)
            ok &= svc.TrySetCpuMaxFrequencyPercent(profile.CpuMaxFrequencyPercent.Value);
        if (profile.CpuBoostMode.HasValue)
            ok &= svc.TrySetCpuBoostMode(profile.CpuBoostMode.Value);
        return ok;
    }

    public bool ApplySafeFallback()
    {
        if (App.ProfileService is null)
        {
            return false;
        }

        TuningProfile fallback = App.ProfileService.GetSafeFallbackProfile();
        bool ok = App.ProfileService.TryApplyPowerPlan(fallback);
        StatusText = ok
            ? $"Applied safe fallback: {fallback.Name}"
            : "Failed to apply safe fallback.";
        Refresh();
        return ok;
    }

    public void DuplicateSelected()
    {
        if (SelectedProfile is null || App.ProfileService is null)
        {
            return;
        }

        var clone = new TuningProfile
        {
            Name = $"{SelectedProfile.Name} Copy",
            TargetWorkload = SelectedProfile.TargetWorkload,
            TargetWorkloadType = SelectedProfile.TargetWorkloadType,
            PreferredPowerPlan = SelectedProfile.PreferredPowerPlan,
            GpuPowerLimitPercent = SelectedProfile.GpuPowerLimitPercent,
            GpuMaxClockMHz = SelectedProfile.GpuMaxClockMHz,
            GpuVoltageMv = SelectedProfile.GpuVoltageMv,
            CpuPptW = SelectedProfile.CpuPptW,
            CpuTdcA = SelectedProfile.CpuTdcA,
            CpuEdcA = SelectedProfile.CpuEdcA,
            CpuMinFrequencyPercent = SelectedProfile.CpuMinFrequencyPercent,
            CpuMaxFrequencyPercent = SelectedProfile.CpuMaxFrequencyPercent,
            CpuBoostMode = SelectedProfile.CpuBoostMode,
            SafetyLimits = SelectedProfile.SafetyLimits
        };

        App.ProfileService.AddProfile(clone);
        Refresh();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Name.Equals(clone.Name, StringComparison.OrdinalIgnoreCase));
        StatusText = $"Duplicated profile: {SelectedProfile?.Name ?? clone.Name}";
    }

    public void DeleteSelected()
    {
        if (SelectedProfile is null || App.ProfileService is null)
        {
            return;
        }

        string name = SelectedProfile.Name;
        bool ok = App.ProfileService.RemoveProfile(name);
        Refresh();
        StatusText = ok ? $"Deleted profile: {name}" : "Cannot delete profile (at least one required).";
    }

    public void ResetDefaults()
    {
        if (App.ProfileService is null)
        {
            return;
        }

        App.ProfileService.ResetToDefaults();
        Refresh();
        StatusText = "Profiles reset to defaults.";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
