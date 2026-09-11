using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ProxyCage.Core;

/// <summary>
/// Хромиум держит пул TCP/QUIC в отдельном процессе NetworkService.
/// Если браузер уже работал до включения защиты, эти сокеты остаются на старом маршруте
/// и сайт открывается «напрямую». Грохать chrome.exe целиком нельзя: панель часто открыта
/// в том же Chrome. Сбрасываем только сетевой процесс — окна остаются, Chrome поднимает
/// новый NetworkService, и свежие соединения уже идут через туннель.
///
/// Остальные изолированные программы — без такого помощника. Процесс не убиваем:
/// окно закроется, а с сеанса 0 его нельзя безопасно открыть снова. Рвём только
/// старые IPv4 TCP, которые ещё не на TUN: клиент переподключается сам.
/// Программы вне списка изоляции не трогаем — их прямой интернет остаётся как был.
/// Служебные учётки и наш движок не трогаем.
/// </summary>
public static class IsolatedAppBounce
{
    public sealed record Report(int Killed, IReadOnlyList<string> Labels, int Connections = 0);

    public static bool IsNetworkServiceCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return false;
        var cmd = commandLine;
        var utility = cmd.Contains("--type=utility", StringComparison.OrdinalIgnoreCase)
                      || cmd.Contains(" /type=utility", StringComparison.OrdinalIgnoreCase);
        if (!utility) return false;
        return cmd.Contains("NetworkService", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTelegramUpdater(string? imagePathOrCommandLine) =>
        !string.IsNullOrWhiteSpace(imagePathOrCommandLine)
        && imagePathOrCommandLine.Contains("Updater.exe", StringComparison.OrdinalIgnoreCase);

    public static bool IsProtectedImage(string? imagePathOrCommandLine)
    {
        if (string.IsNullOrWhiteSpace(imagePathOrCommandLine)) return false;
        var s = imagePathOrCommandLine;
        return Regex.IsMatch(s, @"[\\/]cehoproxy(\.exe)?(\s|""|$)", RegexOptions.IgnoreCase)
               || Regex.IsMatch(s, @"[\\/]ceho-engine\.exe(\s|""|$)", RegexOptions.IgnoreCase)
               || Regex.IsMatch(s, @"[\\/]sing-box\.exe(\s|""|$)", RegexOptions.IgnoreCase);
    }

    public static bool UsesStickyTcpReset(AppEntry app) =>
        app.Enabled && !AppDetector.IsChromiumFamily(app);

    public static bool WouldResetProcess(CehoConfig cfg, string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || IsProtectedImage(imagePath)) return false;
        foreach (var app in cfg.Apps.Where(UsesStickyTcpReset))
        {
            foreach (var rx in AppDetector.ToRegexes(app))
            {
                try
                {
                    if (Regex.IsMatch(imagePath, rx, RegexOptions.IgnoreCase)) return true;
                }
                catch { }
            }
        }

        return false;
    }

    public static bool ShouldResetConnection(string localAddr, string remoteAddr, string tunPrefix)
    {
        if (IsLoopback(localAddr) || IsLoopback(remoteAddr)) return false;
        return string.IsNullOrEmpty(tunPrefix)
               || !localAddr.StartsWith(tunPrefix, StringComparison.Ordinal);
    }

    public static IReadOnlyList<string> ProcessNames(AppEntry app)
    {
        if (AppDetector.IsChrome(app)) return new[] { "chrome" };
        if (AppDetector.IsEdge(app)) return new[] { "msedge" };
        if (AppDetector.IsBrave(app)) return new[] { "brave" };
        if (AppDetector.IsOpera(app)) return new[] { "opera", "launcher" };
        if (AppDetector.IsTelegram(app)) return new[] { "telegram" };
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
                        if (AppIsolation.IsServiceAccount(user)) continue;
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
        var connections = 0;
        var labels = new List<string>();

        foreach (var app in cfg.Apps.Where(a => a.Enabled && AppDetector.IsChromiumFamily(a)))
        {
            var before = killed;
            foreach (var name in ProcessNames(app))
            {
                foreach (var (pid, user, cmd) in Os.WindowsProcessesOwned(name + ".exe"))
                {
                    if (AppIsolation.IsServiceAccount(user)) continue;
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

        var tunPrefix = TunPrefix(cfg.TunAddress);
        foreach (var app in cfg.Apps.Where(UsesStickyTcpReset))
        {
            var before = connections;
            var covered = ProcessInspector.PidsOf(app);
            if (covered.Count == 0) continue;

            foreach (var name in ExeNamesOf(covered))
            {
                foreach (var (pid, user, cmd) in Os.WindowsProcessesOwned(name))
                {
                    if (!covered.Contains(pid)) continue;
                    if (AppIsolation.IsServiceAccount(user)) continue;
                    if (IsTelegramUpdater(cmd) || IsProtectedImage(cmd)) continue;
                    var n = TcpSessions.ResetEstablishedIPv4(pid, tunPrefix, log);
                    if (n <= 0) continue;
                    connections += n;
                    log?.Invoke($"сброшены старые TCP {app.Label}, pid {pid} ({user}): {n}");
                }
            }

            if (connections > before && !labels.Contains(app.Label, StringComparer.OrdinalIgnoreCase))
                labels.Add(app.Label);
        }

        return new Report(killed, labels, connections);
    }

    private static IReadOnlyList<string> ExeNamesOf(IReadOnlySet<int> pids)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pid in pids)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (!string.IsNullOrWhiteSpace(p.ProcessName))
                    names.Add(p.ProcessName + ".exe");
            }
            catch { }
        }

        return names.ToList();
    }

    internal static string TunPrefix(string tunAddress)
    {
        var ip = tunAddress.Split('/')[0];
        var lastDot = ip.LastIndexOf('.');
        return lastDot > 0 ? ip[..(lastDot + 1)] : ip;
    }

    private static bool IsLoopback(string address) =>
        string.IsNullOrWhiteSpace(address)
        || address.StartsWith("127.", StringComparison.Ordinal)
        || address.StartsWith("::ffff:127.", StringComparison.OrdinalIgnoreCase)
        || address is "::1" or "0.0.0.0" or "::";
}
