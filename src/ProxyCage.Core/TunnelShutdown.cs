namespace ProxyCage.Core;

/// <summary>
/// Полное выключение TUN перед обновлением или после залипшего Wintun.
/// </summary>
public static class TunnelShutdown
{
    public sealed record Result(bool Ok, string? ErrorKey, string? Detail)
    {
        public bool DaemonWasRunning { get; init; }
        public bool AutostartWasOn { get; init; }
    }

    public static Result PrepareForUpdate(CehoConfig cfg, string root, string runtimeConfigPath, Action<string>? log = null)
    {
        if (OperatingSystem.IsWindows())
            return PrepareWindowsForUpdate(cfg, root, runtimeConfigPath, log);

        if (DaemonControl.IsRunning(root))
        {
            log?.Invoke("останавливаю службу перед обновлением");
            DaemonControl.RequestStop(root);
            if (!WaitUntilDaemonGone(root, 20000, log))
            {
                TunCleanup.KillOurProcesses(runtimeConfigPath, log);
                WaitUntilDaemonGone(root, 8000, log);
            }
        }

        return Release(cfg, root, runtimeConfigPath, log);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static Result PrepareWindowsForUpdate(
        CehoConfig cfg, string root, string runtimeConfigPath, Action<string>? log)
    {
        var wasRunning = DaemonControl.IsRunning(root);
        var autostartWasOn = Autostart.IsEnabled();

        var result = StopBeforeRelease(
            () => DaemonControl.IsRunning(root),
            () =>
            {
                log?.Invoke("останавливаю службу перед обновлением");
                DaemonControl.RequestStop(root);
            },
            () => WaitUntilDaemonGone(root, 20000, log),
            () => DaemonControl.StopInstalledWindowsDaemons(root),
            () => WaitUntilDaemonGone(root, 8000, log),
            () => Release(cfg, root, runtimeConfigPath, log),
            () => RestoreWindowsDaemon(root, wasRunning, autostartWasOn, log));
        return result with { DaemonWasRunning = wasRunning, AutostartWasOn = autostartWasOn };
    }

    public static async Task<Result> PrepareFromRunningDaemonAsync(
        CehoConfig cfg,
        string root,
        string runtimeConfigPath,
        Action stopLocalTunnel,
        Func<Task> restoreLocalTunnel,
        Action<string>? log = null)
    {
        try { stopLocalTunnel(); }
        catch
        {
            await restoreLocalTunnel();
            throw;
        }

        var result = Release(cfg, root, runtimeConfigPath, log);
        if (!result.Ok) await restoreLocalTunnel();
        return result;
    }

    public static void RestoreAfterUpdateHandoffFailure(Result prepared, string root, Action<string>? log = null)
    {
        if (!OperatingSystem.IsWindows() || !prepared.Ok) return;
        RestoreWindowsDaemon(root, prepared.DaemonWasRunning, prepared.AutostartWasOn, log);
    }

    internal static Result StopBeforeRelease(
        Func<bool> daemonIsRunning,
        Action requestStop,
        Func<bool> waitForGracefulStop,
        Func<bool> forceStop,
        Func<bool> waitForForcedStop,
        Func<Result> release,
        Action restoreDaemon)
    {
        var wasRunning = daemonIsRunning();
        if (wasRunning)
        {
            requestStop();
            if (!waitForGracefulStop())
            {
                if (!forceStop() || !waitForForcedStop())
                {
                    // No routes were touched. If the process is still alive, leave
                    // its existing tunnel alone; if it exited, bring it back.
                    if (!daemonIsRunning()) restoreDaemon();
                    return new Result(false, "upd_need_reboot", "daemon did not stop");
                }
            }
        }

        var result = release();
        if (!result.Ok && wasRunning) restoreDaemon();
        return result;
    }

    private static void RestoreWindowsDaemon(string root, bool wasRunning, bool autostartWasOn, Action<string>? log)
    {
        if (!wasRunning) return;

        if (autostartWasOn)
        {
            Autostart.Restart();
            if (!DaemonControl.WaitUntilRunning(root, 12000))
                log?.Invoke("не удалось восстановить службу после отмены обновления");
            return;
        }

        DaemonControl.ClearRunning(root);
        if (!DaemonControl.StartInBackground(Installer.BinaryPath(root), root))
            log?.Invoke("не удалось восстановить демон после отмены обновления");
    }

    public static Result Release(CehoConfig cfg, string root, string runtimeConfigPath, Action<string>? log = null)
    {
        if (!Os.IsWindows)
        {
            TunCleanup.KillOurProcesses(runtimeConfigPath, log);
            TunCleanup.RemoveLeftovers(log, cfg.TunAddress, root);
            return ResultOk(cfg);
        }

        TunCleanup.ReleaseOurs(runtimeConfigPath, cfg.TunAddress, root, log, attempts: 3, aggressive: true);
        return ResultOk(cfg);
    }

    private static Result ResultOk(CehoConfig cfg)
    {
        if (NodeProbe.TunnelIsUp(cfg.TunAddress))
            return new Result(false, "upd_need_reboot", cfg.TunAddress);
        return new Result(true, null, null);
    }

    private static bool WaitUntilDaemonGone(string root, int timeoutMs, Action<string>? log)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (!DaemonControl.IsRunning(root)) return true;
            Thread.Sleep(500);
        }
        log?.Invoke("служба не успела остановиться");
        return false;
    }
}
