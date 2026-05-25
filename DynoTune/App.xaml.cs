using System.Runtime.Versioning;
using Microsoft.UI.Xaml;
using DynoTune.Services;
using DynoTune.ViewModels;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DynoTune
{
    [SupportedOSPlatform("windows10.0.19041.0")]
    public partial class App : Application
    {
        private Window? _window;

        /// <summary>Single live-data feed shared between MainWindow and MonitoringPage.</summary>
        public static MonitoringViewModel LiveData { get; } = new();
        public static AppSettingsService? SettingsService { get; private set; }
        public static LoggingService? LoggingService { get; private set; }
        public static ProfileService? ProfileService { get; private set; }
        public static WindowsPowerPlanService? PowerPlanService { get; private set; }
        public static AdaptiveOptimizationService? OptimizationService { get; private set; }
        public static ProfileSearchService? ProfileSearchService { get; private set; }
        public static Action? ForceSafeRollbackAction { get; private set; }
        public static Action? ClearDangerStateAction { get; private set; }
        public static Action? StartOptimizationAction { get; private set; }
        public static Action? StopOptimizationAction { get; private set; }
        public static Action<bool, int>? ConfigureOptimizationAutoApplyAction { get; private set; }
        public static Func<bool>? ApplyRecommendedOptimizationAction { get; private set; }
        public static Action? StartProfileSearchAction { get; private set; }
        public static Action? StopProfileSearchAction { get; private set; }
        public static Action? ApplySettingsAction { get; private set; }
        public static TelemetryRepository? TelemetryRepo { get; private set; }

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            _window.Activate();
        }

        public static void ConfigureRuntimeServices(
            AppSettingsService settingsService,
            LoggingService loggingService,
            ProfileService profileService,
            WindowsPowerPlanService powerPlanService,
            AdaptiveOptimizationService optimizationService,
            ProfileSearchService profileSearchService,
            Action forceSafeRollbackAction,
            Action clearDangerStateAction,
            Action startOptimizationAction,
            Action stopOptimizationAction,
            Action<bool, int> configureOptimizationAutoApplyAction,
            Func<bool> applyRecommendedOptimizationAction,
            Action startProfileSearchAction,
            Action stopProfileSearchAction,
            Action applySettingsAction,
            TelemetryRepository telemetryRepo)
        {
            SettingsService = settingsService;
            LoggingService = loggingService;
            ProfileService = profileService;
            PowerPlanService = powerPlanService;
            OptimizationService = optimizationService;
            ProfileSearchService = profileSearchService;
            ForceSafeRollbackAction = forceSafeRollbackAction;
            ClearDangerStateAction = clearDangerStateAction;
            StartOptimizationAction = startOptimizationAction;
            StopOptimizationAction = stopOptimizationAction;
            ConfigureOptimizationAutoApplyAction = configureOptimizationAutoApplyAction;
            ApplyRecommendedOptimizationAction = applyRecommendedOptimizationAction;
            StartProfileSearchAction = startProfileSearchAction;
            StopProfileSearchAction = stopProfileSearchAction;
            ApplySettingsAction = applySettingsAction;
            TelemetryRepo = telemetryRepo;
        }
    }
}
