using Microsoft.UI.Xaml.Controls;
using System.Runtime.Versioning;
using DynoTune.ViewModels;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DynoTune.Views
{
    [SupportedOSPlatform("windows10.0.19041.0")]
    public sealed partial class LogsPage : Page
    {
        private readonly LogsPageViewModel _vm = new();

        public LogsPage()
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

        private async void ExportButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            await _vm.ExportNowAsync();
        }

        private void OpenFolderButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _vm.OpenLatestLogFolder();
        }
    }
}
