using System.Net.NetworkInformation;

namespace ProxyCage.Core;

/// <summary>
/// Проверка условий до запуска. Любая нехватка прав или файла должна всплывать
/// понятной фразой с готовым решением, а не падать невнятной ошибкой посреди работы.
/// </summary>
public static class Preflight
{
    public enum Level { Ok, Warning, Blocker }

    public sealed record Check(Level Level, string Title, string? Detail, string? Fix);

    public static bool IsElevated() => Os.IsElevated();

    public static IReadOnlyList<Check> Run(CehoConfig cfg, string root)
    {
        var l = cfg.Language;
        string S(string key, params object[] a) => Strings.T(l, key, a);

        var checks = new List<Check>();

        if (Os.IsElevated())
            checks.Add(new Check(Level.Ok, S("pf_rights_ok"), null, null));
        else
            checks.Add(new Check(Level.Blocker,
                S(Os.IsWindows ? "pf_rights_need_win" : "pf_rights_need_unix"),
                S("pf_rights_detail"),
                S(Os.IsWindows ? "pf_rights_fix_win" : "pf_rights_fix_unix")));

        var singBox = Os.ResolveSingBox(root);
        if (singBox is not null)
            checks.Add(new Check(Level.Ok, S("pf_engine_ok"), singBox, null));
        else
            checks.Add(new Check(Level.Blocker,
                S("pf_engine_missing", Os.SingBoxFileName),
                S("pf_engine_detail", root),
                Os.IsWindows
                    ? S("pf_engine_fix_win", Os.SingBoxFileName)
                    : S("pf_engine_fix_unix", root)));

        if (Os.ResolveCurl() is not null)
            checks.Add(new Check(Level.Ok, S("pf_curl_ok"), null, null));
        else
            checks.Add(new Check(Level.Warning,
                S("pf_curl_missing"), S("pf_curl_detail"),
                S(Os.IsWindows ? "pf_curl_fix_win" : "pf_curl_fix_unix")));

        if (Os.IsLinux && !File.Exists("/dev/net/tun"))
            checks.Add(new Check(Level.Blocker, S("pf_tun_missing"), S("pf_tun_detail"), S("pf_tun_fix")));

        if (Os.IsLinux && Os.FindOnPath("ip") is null)
            checks.Add(new Check(Level.Blocker, S("pf_ip_missing"), S("pf_ip_detail"), S("pf_ip_fix")));

        checks.Add(CheckWritable(root, l));
        checks.Add(CheckPort(cfg.WebPort, l));

        if (cfg.Subscriptions.Count == 0)
            checks.Add(new Check(Level.Blocker, S("pf_no_subs"), S("pf_no_subs_detail"), S("pf_no_subs_fix")));
        else
            checks.Add(new Check(Level.Ok, S("subs_count", cfg.Subscriptions.Count), null, null));

        var enabled = cfg.Apps.Count(a => a.Enabled);
        if (enabled == 0)
            checks.Add(new Check(Level.Blocker, S("pf_no_apps"), S("pf_no_apps_detail"), S("pf_no_apps_fix")));
        else
            checks.Add(new Check(Level.Ok, S("apps_isolated", enabled), null, null));

        return checks;
    }

    private static Check CheckWritable(string root, string lang)
    {
        try
        {
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, ".write-test");
            File.WriteAllText(probe, "1");
            File.Delete(probe);
            return new Check(Level.Ok, Strings.T(lang, "pf_dir_ok"), null, null);
        }
        catch (Exception ex)
        {
            return new Check(Level.Blocker,
                Strings.T(lang, "pf_dir_bad"),
                $"{root}: {ex.Message}",
                Strings.T(lang, Os.IsWindows ? "pf_dir_fix_win" : "pf_dir_fix_unix"));
        }
    }

    private static Check CheckPort(int port, string lang)
    {
        try
        {
            var busy = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(e => e.Port == port);
            return busy
                ? new Check(Level.Blocker,
                    Strings.T(lang, "pf_port_busy", port),
                    Strings.T(lang, "pf_port_detail"),
                    Strings.T(lang, "pf_port_fix", port + 1))
                : new Check(Level.Ok, Strings.T(lang, "pf_port_ok", port), null, null);
        }
        catch
        {
            return new Check(Level.Warning, Strings.T(lang, "pf_port_unknown", port), null, null);
        }
    }
}
