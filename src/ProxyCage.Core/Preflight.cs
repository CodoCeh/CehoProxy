using System.Net.NetworkInformation;

namespace ProxyCage.Core;

public static class Preflight
{
    public enum Level { Ok, Warning, Blocker }

    /// <summary><see cref="Repair"/> заполнен, если это умеет починить доктор сам.</summary>
    public sealed record Check(Level Level, string Title, string? Detail, string? Fix, Repair Repair = Repair.None);

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
                    : S("pf_engine_fix_unix", root),
                Repair.Engine));

        if (singBox is not null && Installer.MissingCronetDll(root))
            checks.Add(new Check(Level.Blocker,
                S("pf_cronet_missing"),
                S("pf_cronet_detail", root),
                S("engine_update_hint", Os.IsWindows ? "" : "sudo "),
                Repair.Engine));

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
        checks.Add(CheckPort(cfg.WebPort, l, root, panel: true));

        checks.Add(CheckPort(cfg.MixedPort, l, root, panel: false));

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

    /// <summary>
    /// Права спрашиваем у самой папки, а не у системы: где-то она общая и нужен админ,
    /// а где-то (CEHOPROXY_HOME в своей папке) писать можно и обычным пользователем.
    /// </summary>
    public static bool FolderIsWritable(string root, out string why)
    {
        try
        {
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, ".write-test");
            File.WriteAllText(probe, "1");
            File.Delete(probe);
            why = "";
            return true;
        }
        catch (Exception ex)
        {
            why = ex.Message;
            return false;
        }
    }

    private static Check CheckWritable(string root, string lang) =>
        FolderIsWritable(root, out var why)
            ? new Check(Level.Ok, Strings.T(lang, "pf_dir_ok"), null, null)
            : new Check(Level.Blocker,
                Strings.T(lang, "pf_dir_bad"),
                $"{root}: {why}",
                Strings.T(lang, Os.IsWindows ? "pf_dir_fix_win" : "pf_dir_fix_unix"));

    public static int NextFreePort(int from)
    {
        try
        {
            var busy = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Select(e => e.Port)
                .ToHashSet();
            for (var p = from + 1; p < 65535; p++)
                if (!busy.Contains(p)) return p;
        }
        catch { }
        return from + 1;
    }

    private static Check CheckPort(int port, string lang, string root, bool panel)
    {
        try
        {
            var busy = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(e => e.Port == port);

            if (busy && DaemonControl.IsRunning(root))
                return new Check(Level.Ok, Strings.T(lang, "pf_port_ours", port), null, null);

            if (!busy)
                return new Check(Level.Ok,
                    Strings.T(lang, panel ? "pf_port_ok" : "pf_proxy_port_ok", port), null, null);

            return new Check(Level.Blocker,
                Strings.T(lang, panel ? "pf_port_busy" : "pf_proxy_port_busy", port),
                Strings.T(lang, panel ? "pf_port_detail" : "pf_proxy_port_detail"),
                Strings.T(lang, panel ? "pf_port_fix" : "pf_proxy_port_fix", NextFreePort(port)),
                panel ? Repair.PanelPort : Repair.ProxyPort);
        }
        catch
        {
            return new Check(Level.Warning, Strings.T(lang, "pf_port_unknown", port), null, null);
        }
    }
}
