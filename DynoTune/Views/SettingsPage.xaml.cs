using System.Runtime.Versioning;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DynoTune.ViewModels;

namespace DynoTune.Views;

[SupportedOSPlatform("windows10.0.19041.0")]
public sealed partial class SettingsPage : Page
{
    private readonly SettingsViewModel _vm = new();

    public SettingsPage()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e) => _vm.Load();

    private void ApplyButton_Click(object sender, RoutedEventArgs e) => _vm.ApplyAndSave();

    private void ResetButton_Click(object sender, RoutedEventArgs e) => _vm.ResetToDefaults();
}
