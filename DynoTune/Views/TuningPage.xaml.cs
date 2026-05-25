using Microsoft.UI.Xaml.Controls;
using System;
using System.Runtime.Versioning;
using DynoTune.ViewModels;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DynoTune.Views
{
    [SupportedOSPlatform("windows10.0.19041.0")]
    public sealed partial class TuningPage : Page
    {
        private readonly TuningPageViewModel _vm = new();

        public TuningPage()
        {
            InitializeComponent();
            DataContext = _vm;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _vm.Refresh();
            App.LiveData.Refreshed += LiveData_Refreshed;
            RefreshDangerTexts();
        }

        private void OnUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            App.LiveData.Refreshed -= LiveData_Refreshed;
        }

        private void LiveData_Refreshed(object? sender, EventArgs e)
        {
            _vm.SyncRuntimeState();
            RefreshDangerTexts();
        }

        private void RefreshDangerTexts()
        {
            OptimizerPhaseText.Text = $"Optimizer Phase: {_vm.OptimizerPhase}";
            OptimizerRunStatusText.Text = _vm.OptimizationRunStatusText;
            OptimizerBaselineText.Text = $"Baseline: {_vm.OptimizerBaseline}";
            OptimizerRecommendationText.Text = $"Recommendation: {_vm.OptimizerRecommendation}";
            OptimizerDecisionText.Text = $"Last Decision: {_vm.OptimizerDecision}";
            SearchPhaseText.Text = $"Search Phase: {_vm.SearchPhase}";
            SearchWorkloadText.Text = $"Search Workload: {_vm.SearchWorkload}";
            SearchCandidateText.Text = $"Search Candidate: {_vm.SearchCurrentCandidate}";
            SearchBestText.Text = $"Search Best: {_vm.SearchBestCandidate}";
            SearchDecisionText.Text = $"Search Decision: {_vm.SearchDecision}";
            DangerLevelText.Text = $"Danger: {App.LiveData.DangerLevel}";
            DangerReasonText.Text = $"Reason: {App.LiveData.DangerReason}";
            DangerDetailText.Text = $"Detail: {App.LiveData.DangerReasonDetail}";
            DangerRollbackText.Text = $"Rollback Applied: {App.LiveData.DangerRollbackApplied}";
        }

        private void ApplyPlanButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _vm.ApplySelectedPowerPlan();
        }

        private void ApplyProfileButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _vm.ApplySelectedProfile();
        }

        private void ForceSafeButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _vm.ForceSafeRollback();
            RefreshDangerTexts();
        }

        private void ClearDangerButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _vm.ClearDangerState();
            RefreshDangerTexts();
        }

        private void RefreshButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _vm.Refresh();
            RefreshDangerTexts();
        }

        private void StartOptimizationButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _vm.StartOptimization();
        }

        private void StopOptimizationButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _vm.StopOptimization();
        }

        private void ApplyRecommendedButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _vm.ApplyRecommended();
            RefreshDangerTexts();
        }

        private void RollbackVendorButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _vm.RollbackToVendorSafe();
            RefreshDangerTexts();
        }

        private void StartProfileSearchButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _vm.StartProfileSearch();
            RefreshDangerTexts();
        }

        private void StopProfileSearchButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _vm.StopProfileSearch();
            RefreshDangerTexts();
        }
    }
}
