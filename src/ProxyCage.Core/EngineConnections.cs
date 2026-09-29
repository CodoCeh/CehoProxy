using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ProxyCage.Core;

public static class EngineConnections
{
    public sealed record Connection(string ProcessPath, bool Direct);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

    public static IReadOnlyList<Connection> Fetch(int clashApiPort)
    {
        try
        {
            var json = Http.GetStringAsync($"http://127.0.0.1:{clashApiPort}/connections").GetAwaiter().GetResult();
            return Parse(json);
        }
        catch { return Array.Empty<Connection>(); }
    }

    internal static IReadOnlyList<Connection> Parse(string json)
    {
        var list = new List<Connection>();
        if (JsonNode.Parse(json)?["connections"] is not JsonArray connections) return list;
        foreach (var c in connections)
        {
            var path = (string?)c?["metadata"]?["processPath"];
            if (string.IsNullOrEmpty(path)) continue;
            var chains = c!["chains"] as JsonArray;
            var direct = chains is not null && chains.Any(x => (string?)x == "direct");
            list.Add(new(path, direct));
        }
        return list;
    }

    public static (int Vpn, int Direct) CountFor(AppEntry app, IReadOnlyList<Connection> connections)
    {
        if (connections.Count == 0) return (0, 0);
        var rxes = AppDetector.ToRegexes(app)
            .Select(r => new Regex(r, Os.IsLinux ? RegexOptions.None : RegexOptions.IgnoreCase))
            .ToList();
        int vpn = 0, direct = 0;
        foreach (var c in connections)
        {
            if (!rxes.Any(rx => rx.IsMatch(c.ProcessPath))) continue;
            if (c.Direct) direct++; else vpn++;
        }
        return (vpn, direct);
    }
}
