namespace ProxyCage.Core;

/// <summary>
/// Полное выключение TUN перед обновлением или после залипшего Wintun.
/// </summary>
public static class TunnelShutdown
{
    public sealed record Result(bool Ok, string? ErrorKey, string? Detail);

    public static Result PrepareForUpdate(CehoConfig cfg, string root, string runtimeConfigPath, Action<string>? log = null)
    {
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
