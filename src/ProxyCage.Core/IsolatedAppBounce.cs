using System.Diagnostics;

namespace ProxyCage.Core;

/// <summary>
/// Хромиум держит пул TCP/QUIC в отдельном процессе NetworkService.
/// Если браузер уже работал до включения защиты, эти сокеты остаются на старом маршруте
/// и сайт открывается «напрямую». Грохать chrome.exe целиком нельзя: панель часто открыта
/// в том же Chrome. Сбрасываем только сетевой процесс — окна остаются, Chrome поднимает
/// новый NetworkService, и свежие соединения уже идут через туннель.
/// </summary>
public static class IsolatedAppBounce
{
    public sealed record Report(int Killed, IReadOnlyList<string> Labels);

    public static bool IsNetworkServiceCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return false;
        var cmd = commandLine;
        var utility = cmd.Contains("--type=utility", StringComparison.OrdinalIgnoreCase)
                      || cmd.Contains(" /type=utility", StringComparison.OrdinalIgnoreCase);
        if (!utility) return false;
        return cmd.Contains("NetworkService", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> ProcessNames(AppEntry app)
    {
        if (AppDetector.IsChrome(app)) return new[] { "chrome" };
        if (AppDetector.IsEdge(app)) return new[] { "msedge" };
        if (AppDetector.IsBrave(app)) return new[] { "brave" };
        if (AppDetector.IsOpera(app)) return new[] { "opera", "launcher" };
        return Array.Empty<string>();
    }

    public static IReadOnlyList<string> RunningBrowserLabels(CehoConfig cfg)
    {
        var labels = new List<string>();
        foreach (var app in cfg.Apps.Where(a => a.Enabled && AppDetector.IsChromiumFamily(a)))
        {
            foreach (var name in ProcessNames(app))
            {
                var mine = false;
                if (Os.IsWindows)
                {
                    foreach (var (_, user, _) in Os.WindowsProcessesOwned(name + ".exe"))
                    {
                        if (!AppIsolation.IsServiceAccount(user)
                            && !AppIsolation.SameWindowsUser(AppIsolation.CurrentUser(), user))
                            continue;
                        if (Os.IsWindowsServer && AppIsolation.IsServiceAccount(user))
                            continue;
                        mine = true;
                        break;
                    }
                }
                else
                {
                    Process[] ps;
                    try { ps = Process.GetProcessesByName(name); }
                    catch { continue; }
                    mine = ps.Length > 0;
                    foreach (var p in ps) p.Dispose();
                }

                if (!mine) continue;
                labels.Add(app.Label);
                break;
            }
        }
        return labels;
    }

    public static Report ResetNetwork(CehoConfig cfg, Action<string>? log = null)
    {
        if (!Os.IsWindows) return new Report(0, Array.Empty<string>());

        var killed = 0;
        var labels = new List<string>();

        foreach (var app in cfg.Apps.Where(a => a.Enabled && AppDetector.IsChromiumFamily(a)))
        {
            var before = killed;
            var owner = AppIsolation.CurrentUser();
            foreach (var name in ProcessNames(app))
            {
                foreach (var (pid, user, cmd) in Os.WindowsProcessesOwned(name + ".exe"))
                {
                    if (Os.IsWindowsServer)
                    {
                        if (AppIsolation.IsServiceAccount(user)) continue;
                        if (!AppIsolation.SameWindowsUser(owner, user)) continue;
                    }
                    else if (!AppIsolation.IsServiceAccount(user)
                             && !AppIsolation.SameWindowsUser(owner, user))
                        continue;
                    if (!IsNetworkServiceCommandLine(cmd)) continue;
                    try
                    {
                        using var p = Process.GetProcessById(pid);
                        p.Kill();
                        killed++;
                        log?.Invoke($"сброшен сетевой процесс {app.Label}, pid {pid} ({user})");
                    }
                    catch (Exception ex)
                    {
                        log?.Invoke($"не удалось сбросить pid {pid} ({app.Label}): {ex.Message}");
                    }
                }
            }
            if (killed > before && !labels.Contains(app.Label, StringComparer.OrdinalIgnoreCase))
                labels.Add(app.Label);
        }

        return new Report(killed, labels);
    }
}
