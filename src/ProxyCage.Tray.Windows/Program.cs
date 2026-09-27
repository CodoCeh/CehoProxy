using System.Runtime.Versioning;
using ProxyCage.Core;

namespace ProxyCage.Tray.Windows;

[SupportedOSPlatform("windows")]
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var single = new Mutex(true, @"Local\CehoProxyTray", out var first);
        if (!first) return;

        using var tray = new TrayWindow(
            Environment.GetEnvironmentVariable("CEHOPROXY_HOME") ?? Os.DefaultRoot);
        tray.Run();
    }
}
