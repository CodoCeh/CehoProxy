using System.Runtime.Versioning;
using ProxyCage.Core;

namespace ProxyCage.Tray.Windows;

[SupportedOSPlatform("windows")]
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var single = new Mutex(false, @"Local\CehoProxyTray");
        bool owned;
        try { owned = single.WaitOne(args.Contains("--restart") ? TimeSpan.FromSeconds(20) : TimeSpan.Zero); }
        catch (AbandonedMutexException) { owned = true; }
        if (!owned) return;

        using var tray = new TrayWindow(
            Environment.GetEnvironmentVariable("CEHOPROXY_HOME") ?? Os.DefaultRoot);
        tray.Run();
    }
}
