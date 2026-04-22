using System.Runtime.Versioning;
using Microsoft.UI.Xaml;
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

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            _window.Activate();
        }
    }
}
