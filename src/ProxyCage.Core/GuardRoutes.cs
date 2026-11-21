namespace ProxyCage.Core;

public static class GuardRoutes
{
    public static readonly string[] V4Prefixes =
    [
        "1.0.0.0/8", "2.0.0.0/7", "4.0.0.0/6", "8.0.0.0/5",
        "16.0.0.0/4", "32.0.0.0/3", "64.0.0.0/2", "128.0.0.0/1",
    ];

    private static readonly char[] Blanks = [' ', '\t'];
    private static readonly object Gate = new();
    private static string? _interface;
    private static string[] _v6 = [];

    public static string? Interface { get { lock (Gate) return _interface; } }

    public static string? FindInterface(string ifconfig, string address)
    {
        address = address.Split('/')[0];
        string? current = null;
        foreach (var raw in ifconfig.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                var colon = line.IndexOf(':');
                current = colon > 0 ? line[..colon] : null;
                continue;
            }

            var parts = line.Split(Blanks, StringSplitOptions.RemoveEmptyEntries);
            if (current is not null && parts.Length >= 2 && parts[0] is "inet" or "inet6"
                && parts[1].Split('%')[0].Equals(address, StringComparison.OrdinalIgnoreCase))
                return current;
        }
        return null;
    }

    public static IReadOnlyList<string> ParseV6Prefixes(string netstat, string iface)
    {
        var found = new List<string>();
        foreach (var raw in netstat.Split('\n'))
        {
            var cols = raw.Split(Blanks, StringSplitOptions.RemoveEmptyEntries);
            if (cols.Length < 4 || cols[3] != iface) continue;
            var dest = cols[0];
            if (!dest.Contains('/') || dest.StartsWith("fe80", StringComparison.OrdinalIgnoreCase)
                || dest.StartsWith("ff", StringComparison.OrdinalIgnoreCase) || dest.Contains('%')) continue;
            if (cols[2].Contains('H')) continue;
            found.Add(dest);
        }
        return found;
    }

    public static IReadOnlyList<string> Commands(string iface, IEnumerable<string> v6)
    {
        var list = new List<string>();
        foreach (var prefix in V4Prefixes) list.Add($"-n add -net {prefix} -interface {iface}");
        foreach (var prefix in v6) list.Add($"-n add -inet6 {prefix} -interface {iface}");
        return list;
    }

    public static bool Learn(string address, bool installed, Action<string>? log = null)
    {
        if (!Os.IsMac) return false;
        var iface = FindInterface(Os.Run("ifconfig", "", 10000).Output, address);
        if (iface is null)
        {
            log?.Invoke("адаптер сторожа не найден: маршруты вернуть нечем");
            return false;
        }

        lock (Gate)
        {
            _interface = iface;
            if (installed) _v6 = ParseV6Prefixes(Os.Run("netstat", "-rn -f inet6", 10000).Output, iface).ToArray();
        }
        return true;
    }

    public static async Task<bool> LearnAsync(string address, bool installed, Action<string>? log = null, int attempts = 20)
    {
        if (!Os.IsMac) return false;
        for (var i = 0; i < attempts; i++)
        {
            if (Learn(address, installed, i == attempts - 1 ? log : null)) return true;
            await Task.Delay(250);
        }
        return false;
    }

    public static int Engage(Action<string>? log = null)
    {
        if (!Os.IsMac) return 0;
        string? iface;
        string[] v6;
        lock (Gate) { iface = _interface; v6 = _v6; }
        if (iface is null) return 0;

        var added = 0;
        foreach (var args in Commands(iface, v6))
            if (Os.Run("route", args, 5000).Code == 0) added++;
        if (added > 0) log?.Invoke($"маршруты сторожа возвращены ({added}) на {iface}");
        return added;
    }

    public static void Forget()
    {
        lock (Gate) { _interface = null; _v6 = []; }
    }
}
