using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ProxyCage.Core;

public static class DaemonControl
{
    private const string WindowsEventName = @"Global\CehoProxyStop";
    private const int SIGTERM = 15;

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int PosixKill(int pid, int sig);

    private static readonly string[] OurProcessNames = { "cehoproxy", "chp" };

    public static string PidPath(string root) => Path.Combine(root, "cehoproxy.pid");

    public static int? RunningPid(string root)
    {
        try
        {
            var path = PidPath(root);
            if (!File.Exists(path)) return null;
            if (!int.TryParse(File.ReadAllText(path).Trim(), out var pid)) return null;

            using var p = Process.GetProcessById(pid);

            return OurProcessNames.Any(n => p.ProcessName.Equals(n, StringComparison.OrdinalIgnoreCase))
                ? pid : null;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsRunning(string root) => RunningPid(root) is not null;

    public static void MarkRunning(string root)
    {
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(PidPath(root), Environment.ProcessId.ToString());
        }
        catch { }
    }

    public static void ClearRunning(string root)
    {
        try { File.Delete(PidPath(root)); } catch { }
    }

    public static bool RequestStop(string root)
    {
        if (OperatingSystem.IsWindows()) return RequestStopWindows();

        var pid = RunningPid(root);
        if (pid is null) return false;
        return PosixKill(pid.Value, SIGTERM) == 0;
    }

    [SupportedOSPlatform("windows")]
    private static bool RequestStopWindows()
    {
        if (!EventWaitHandle.TryOpenExisting(WindowsEventName, out var ev)) return false;
        using (ev) ev.Set();
        return true;
    }

    public static void WaitForStop()
    {
        if (OperatingSystem.IsWindows())
        {
            using var stopEvent = new EventWaitHandle(false, EventResetMode.ManualReset, WindowsEventName);
            stopEvent.Reset();
            stopEvent.WaitOne();
            return;
        }

        using var stop = new ManualResetEventSlim(false);

        using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; stop.Set(); });
        using var intr = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx => { ctx.Cancel = true; stop.Set(); });
        using var hup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, ctx => { ctx.Cancel = true; stop.Set(); });
        stop.Wait();
    }
}
