using Microsoft.UI.Xaml.Controls;
using System.Runtime.Versioning;
using DynoTune.ViewModels;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DynoTune.Views
{
    [SupportedOSPlatform("windows10.0.19041.0")]
    public sealed partial class ProfilesPage : Page
    {
        private readonly ProfilesPageViewModel _vm = new();

        public ProfilesPage()
        {
            InitializeComponent();
            DataContext = _vm;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _vm.Refresh();
        }

        private void RefreshButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _vm.Refresh();
        }

        private void SetActiveButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _vm.SetActiveSelected();
        }

        private void ApplyButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _vm.ApplySelected();
        }

        private void DuplicateButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _vm.DuplicateSelected();
        }

        private void DeleteButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _vm.DeleteSelected();
        }

        private void SafeFallbackButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _vm.ApplySafeFallback();
        }
    }
}
