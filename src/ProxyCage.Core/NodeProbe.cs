using System.Diagnostics;
using System.Net.Sockets;

namespace ProxyCage.Core;

public static class NodeProbe
{
    public sealed record Measured(ProxyNode Node, int? LatencyMs);

    public sealed record CountryRow(
        string Code, string? Name, int Nodes, int Alive, int? BestMs, IReadOnlyList<Measured> Items);

    /// <summary>
    /// Прямой TCP-замер имеет смысл только когда наш движок не поднят:
    /// иначе TUN принимает соединения локально и цифры врут.
    /// </summary>
    public static bool MeasureBlocked(string tunAddress, bool engineRunning) =>
        engineRunning && TunnelIsUp(tunAddress);

    public static int? LatencyFor(
        ProxyNode node,
        CehoConfig cfg,
        IReadOnlyDictionary<string, int>? liveByTag = null)
    {
        if (liveByTag?.TryGetValue(node.Tag, out var live) == true && live > 0)
            return live;
        return cfg.NodeLatency.TryGetValue(node.Key, out var saved) ? saved : null;
    }

    public static IReadOnlyList<CountryRow> Summarize(
        IReadOnlyList<ProxyNode> nodes,
        CehoConfig cfg,
        IReadOnlyDictionary<string, int>? liveByTag = null)
    {
        var real = nodes.Where(n => !n.IsMeta).ToList();
        var measured = real
            .Select(n => new Measured(n, LatencyFor(n, cfg, liveByTag)))
            .ToList();
        return GroupByCountry(measured);
    }

    public static async Task<Measured> MeasureAsync(ProxyNode node, int timeoutMs = 2500)
    {
        if (node.Protocol is ProxyProtocol.Hysteria2 or ProxyProtocol.Tuic)
            return new Measured(node, null);

        try
        {
            var address = await ResolveAsync(node.Server, timeoutMs);
            if (address is null) return new Measured(node, null);

            var sw = Stopwatch.StartNew();
            using var client = new TcpClient();
            var connect = client.ConnectAsync(address, node.Port);
            await Task.WhenAny(connect, Task.Delay(timeoutMs));
            if (!client.Connected) return new Measured(node, null);
            return new Measured(node, (int)sw.ElapsedMilliseconds);
        }
        catch
        {
            return new Measured(node, null);
        }
    }

    private static async Task<System.Net.IPAddress?> ResolveAsync(string host, int timeoutMs)
    {
        if (System.Net.IPAddress.TryParse(host, out var parsed)) return parsed;
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var addresses = await DirectDnsResolver.ResolveAsync(host, cts.Token);
            return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                   ?? addresses.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public static bool LooksLikeLocalAccept(IReadOnlyList<Measured> measured)
    {
        const int ImpossiblyFastMs = 10;

        var real = measured.Where(m => m.LatencyMs is not null).ToList();
        if (real.Count < 3) return false;
        if (real.Select(m => m.Node.Server).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 3) return false;

        var countries = real.Select(m => m.Node.CountryCode)
            .Where(c => c is not null).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (countries < 2) return false;

        return real.Count(m => m.LatencyMs < ImpossiblyFastMs) * 10 >= real.Count * 7;
    }

    public static bool TunnelIsUp(string tunAddress)
    {
        var ip = tunAddress.Split('/')[0];
        try
        {
            return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(i => i.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                .SelectMany(i => i.GetIPProperties().UnicastAddresses)
                .Any(a => a.Address.ToString() == ip);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<IReadOnlyList<CountryRow>> ByCountryAsync(
        IReadOnlyList<ProxyNode> nodes, int timeoutMs = 2500)
    {
        var real = nodes.Where(n => !n.IsMeta).ToList();
        var measured = await Task.WhenAll(real.Select(n => MeasureAsync(n, timeoutMs)));
        return GroupByCountry(measured);
    }

    private static IReadOnlyList<CountryRow> GroupByCountry(IReadOnlyList<Measured> measured) =>
        measured
            .GroupBy(m => m.Node.CountryCode ?? CountryResolver.Unknown)
            .Select(g => new CountryRow(
                g.Key,
                g.First().Node.CountryName,
                g.Count(),
                g.Count(m => m.LatencyMs is not null),
                g.Where(m => m.LatencyMs is not null).Select(m => m.LatencyMs!.Value)
                 .DefaultIfEmpty(int.MaxValue).Min() is var best && best == int.MaxValue ? null : best,
                g.OrderBy(m => m.LatencyMs ?? int.MaxValue).ToList()))
            .OrderBy(r => r.BestMs ?? int.MaxValue)
            .ToList();
}
