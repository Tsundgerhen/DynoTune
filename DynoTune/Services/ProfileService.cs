using System.Diagnostics;
using System.Linq;
using DynoTune.Models;

namespace DynoTune.Services;

/// <summary>
/// In-memory tuning profiles and safe application of Windows power plan (GPU changes stay explicit elsewhere).
/// </summary>
public class ProfileService
{
    private readonly WindowsPowerPlanService _powerPlanService;
    private readonly List<TuningProfile> _profiles = new();
    private TuningProfile? _activeProfile;

    public ProfileService(WindowsPowerPlanService powerPlanService)
    {
        _powerPlanService = powerPlanService;
        SeedDefaults();
        _activeProfile = _profiles.FirstOrDefault(p => p.Name == "Balanced");
    }

    public IReadOnlyList<TuningProfile> Profiles => _profiles;

    public TuningProfile? ActiveProfile => _activeProfile;

    public void SetActiveProfile(string profileName)
    {
        TuningProfile? p = _profiles.FirstOrDefault(x =>
            string.Equals(x.Name, profileName, StringComparison.OrdinalIgnoreCase));
        if (p is not null)
        {
            _activeProfile = p;
        }
    }

    /// <summary>
    /// Picks a profile that targets this classification (fine type first, then coarse class).
    /// </summary>
    public TuningProfile? SuggestProfile(ClassificationResult classification)
    {
        TuningProfile? byType = _profiles.FirstOrDefault(p =>
            p.TargetWorkloadType == classification.WorkloadType);
        if (byType is not null)
        {
            return byType;
        }

        return _profiles.FirstOrDefault(p =>
            p.TargetWorkloadType is null &&
            p.TargetWorkload == classification.CoarseWorkloadClass);
    }

    /// <summary>
    /// Applies Windows power plan from the profile. Does not change GPU clocks (use <see cref="AmdAdlxService"/> separately).
    /// </summary>
    public bool TryApplyPowerPlan(TuningProfile profile)
    {
        if (!_powerPlanService.TrySetActivePlan(profile.PreferredPowerPlan))
        {
            Debug.WriteLine($"ProfileService: could not set power plan to {profile.PreferredPowerPlan} (admin rights?).");
            return false;
        }

        return true;
    }

    private void SeedDefaults()
    {
        _profiles.Add(new TuningProfile
        {
            Name = "Balanced",
            TargetWorkload = WorkloadClass.Mixed,
            TargetWorkloadType = null,
            PreferredPowerPlan = WindowsPowerPlanKind.Balanced
        });

        _profiles.Add(new TuningProfile
        {
            Name = "Idle / saver",
            TargetWorkload = WorkloadClass.Idle,
            TargetWorkloadType = WorkloadType.Idle,
            PreferredPowerPlan = WindowsPowerPlanKind.PowerSaver
        });

        _profiles.Add(new TuningProfile
        {
            Name = "Gaming",
            TargetWorkload = WorkloadClass.GpuHeavy,
            TargetWorkloadType = WorkloadType.Gaming,
            PreferredPowerPlan = WindowsPowerPlanKind.HighPerformance
        });

        _profiles.Add(new TuningProfile
        {
            Name = "Heavy compute",
            TargetWorkload = WorkloadClass.CpuHeavy,
            TargetWorkloadType = WorkloadType.HeavyCompute,
            PreferredPowerPlan = WindowsPowerPlanKind.HighPerformance
        });
    }
}
