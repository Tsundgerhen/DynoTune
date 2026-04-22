using System;
using System.Runtime.Versioning;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.DynamicDependency;

namespace DynoTune;

 [SupportedOSPlatform("windows10.0.19041.0")]
public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // Windows App SDK bootstrap for unpackaged execution.
        // 0x00010008 = major.minor 1.8
        Bootstrap.Initialize(0x00010008);
        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }
}
