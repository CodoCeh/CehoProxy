using System.Net;
using System.Text.RegularExpressions;

namespace ProxyCage.Core;

public static class LeakGuard
{
    public const string RuleName = "CehoProxy leak guard";
    private const string StateFile = "leak-guard.txt";
    private const int MaxProgramsPerApp = 500;

    internal const string PublicRemote =
        "1.0.0.0-9.255.255.255,11.0.0.0-100.63.255.255,100.128.0.0-126.255.255.255," +
        "128.0.0.0-169.253.255.255,169.255.0.0-172.15.255.255,172.32.0.0-192.167.255.255," +
        "192.169.0.0-223.255.255.255,2000::-3fff:ffff:ffff:ffff:ffff:ffff:ffff:ffff";

    public static bool IsActive(string root) => File.Exists(Path.Combine(root, StateFile));

    public static void SetTunnelGuard(string root, bool active)
    {
        if (Os.IsWindows) return;
        var file = Path.Combine(root, StateFile);
        try
        {
            if (active) File.WriteAllText(file, "tun");
            else File.Delete(file);
        }
        catch { }
    }

    public static string? Apply(CehoConfig cfg, string root)
    {
        if (!Os.IsWindows) return null;

        var programs = Programs(cfg);
        var local = OutsideTun(cfg.TunAddress);
        var state = string.Join("\n", programs.Prepend(local));
        var stateFile = Path.Combine(root, StateFile);
        try
        {
            if (File.Exists(stateFile) && File.ReadAllText(stateFile) == state
                && Os.Run("netsh", $"advfirewall firewall show rule name=\"{RuleName}\"", 30000).Code == 0)
                return null;
        }
        catch { }

        Os.Run("netsh", $"advfirewall firewall delete rule name=\"{RuleName}\"", 30000);
        if (programs.Count == 0)
        {
            try { File.Delete(stateFile); } catch { }
            return null;
        }

        var failed = new List<string>();
        foreach (var program in programs)
        {
            var (code, output) = Os.Run("netsh",
                $"advfirewall firewall add rule name=\"{RuleName}\" dir=out action=block enable=yes profile=any " +
                $"program=\"{program}\" localip={local} remoteip={PublicRemote}", 30000);
            if (code != 0) failed.Add($"{program}: {output.Trim()}");
        }

        if (failed.Count > 0)
        {
            try { File.Delete(stateFile); } catch { }
            var error = string.Join("; ", failed);
            Log.Warn($"защита от утечки: не удалось добавить правила брандмауэра: {error}");
            return error;
        }

        File.WriteAllText(stateFile, state);
        Log.Info($"защита от утечки: правила брандмауэра для {programs.Count} файлов");
        return null;
    }

    public static void Remove(string root)
    {
        if (Os.IsWindows)
        {
            Os.Run("netsh", $"advfirewall firewall delete rule name=\"{RuleName}\"", 30000);
            // Правило «sing-tun (путь\ceho-engine.exe)» заводит сам движок и убирает при мягком выходе;
            // после принудительной остановки оно остаётся.
            var like = root.TrimEnd('\\').Replace("'", "''");
            Os.Run("powershell",
                "-NoProfile -Command \"Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -like 'sing-tun (*' -and $_.DisplayName -like '*" + like + "*' } | Remove-NetFirewallRule -ErrorAction SilentlyContinue\"",
                60000);
        }
        try { File.Delete(Path.Combine(root, StateFile)); } catch { }
    }

    internal static IReadOnlyList<string> Programs(CehoConfig cfg)
    {
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var running = ProcessInspector.RunningPaths().ToList();

        foreach (var app in cfg.Apps.Where(a => a.Enabled && !AppDetector.CoversSystemFolder(a)))
        {
            var rxes = AppDetector.ToRegexes(app).Select(r => new Regex(r, RegexOptions.IgnoreCase)).ToList();
            bool Matches(string path) => rxes.Any(rx => rx.IsMatch(path));

            foreach (var exe in ExecutablesUnder(app).Where(Matches).Take(MaxProgramsPerApp))
                found.Add(exe);
            foreach (var exe in running.Where(Matches))
                found.Add(exe);
        }

        return found.ToList();
    }

    private static IEnumerable<string> ExecutablesUnder(AppEntry app)
    {
        var folder = app.Folder.TrimEnd('\\', '/');
        if (app.SingleFile || folder.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(folder)) yield return folder;
            if (app.SingleFile) yield break;
            folder = Path.GetDirectoryName(folder) ?? folder;
        }

        var roots = new List<string>();
        var parent = Path.GetDirectoryName(folder);
        var package = Path.GetFileName(folder);
        var underscore = package.IndexOf('_');
        if (app.VersionAgnostic && parent is not null && underscore > 0 && Directory.Exists(parent))
        {
            try { roots.AddRange(Directory.EnumerateDirectories(parent, package[..underscore] + "_*")); }
            catch { }
        }
        if (roots.Count == 0) roots.Add(folder);

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var root in roots.Where(Directory.Exists))
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*.exe", options).ToList(); }
            catch { continue; }
            foreach (var f in files) yield return f;
        }
    }

    internal static string OutsideTun(string tunCidr)
    {
        var slash = tunCidr.IndexOf('/');
        var address = IPAddress.Parse(slash < 0 ? tunCidr : tunCidr[..slash]);
        var bits = slash < 0 ? 32 : int.Parse(tunCidr[(slash + 1)..]);
        var value = ToUInt(address);
        var mask = bits == 0 ? 0u : uint.MaxValue << (32 - bits);
        var first = value & mask;
        var last = first | ~mask;

        var ranges = new List<string>();
        if (first > 0) ranges.Add($"0.0.0.0-{FromUInt(first - 1)}");
        if (last < uint.MaxValue) ranges.Add($"{FromUInt(last + 1)}-255.255.255.255");
        ranges.Add("::-ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff");
        return string.Join(",", ranges);
    }

    private static uint ToUInt(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
    }

    private static string FromUInt(uint v) => $"{v >> 24}.{(v >> 16) & 255}.{(v >> 8) & 255}.{v & 255}";
}
