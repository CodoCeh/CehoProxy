using System.Diagnostics;
using System.Net.Sockets;

namespace ProxyCage.Core;

/// <summary>Замер задержки до нод, чтобы можно было выбрать страну осознанно.</summary>
public static class NodeProbe
{
    public sealed record Measured(ProxyNode Node, int? LatencyMs);

    public sealed record CountryRow(
        string Code, string? Name, int Nodes, int Alive, int? BestMs, IReadOnlyList<Measured> Items);

    /// <summary>
    /// TCP-хендшейк до server:port. Для QUIC-протоколов (hysteria2, tuic) это UDP,
    /// и TCP-проба соврёт, поэтому такие ноды помечаются неизмеренными, а не мёртвыми.
    /// </summary>
    public static async Task<Measured> MeasureAsync(ProxyNode node, int timeoutMs = 2500)
    {
        if (node.Protocol is ProxyProtocol.Hysteria2 or ProxyProtocol.Tuic)
            return new Measured(node, null);

        try
        {
            // адрес разрешаем ДО секундомера: иначе у нод, записанных именем, в замер
            // попадает время DNS, а у нод, записанных адресом, — нет, и числа несравнимы.
            // Поймано живьём: одна и та же подписка давала 0 мс по адресам и 80 мс по именам
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
            var lookup = System.Net.Dns.GetHostAddressesAsync(host);
            var done = await Task.WhenAny(lookup, Task.Delay(timeoutMs));
            return done == lookup ? lookup.Result.FirstOrDefault() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Замер принимается локально, а не сетью.
    ///
    /// Опознаём по физике: ноды в РАЗНЫХ странах не могут отвечать одинаково быстро —
    /// расстояние разное, и до Хельсинки с Москвой не бывает по 5 мс до обеих. Если почти
    /// все замеры уложились в единицы миллисекунд, значит соединение принимает не нода,
    /// а туннель на этой машине: свой TUN мы ловим по адресу, но у человека может быть
    /// поднят и чужой VPN. Поймано живьём: 11 нод от Хельсинки до Москвы — 0-6 мс.
    ///
    /// Провайдера с нодами в одном городе это не задевает: там мало стран, и правило молчит.
    /// Отсеивать по таким числам нельзя — они не про скорость нод.
    /// </summary>
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

    /// <summary>
    /// При поднятом TUN замер бессмысленен: gvisor принимает TCP-соединение локально и
    /// рапортует успех, поэтому ЛЮБАЯ нода выглядит живой с задержкой в пару миллисекунд.
    /// Проверено: мёртвая нода, которую подписка сама помечает «тех. работы», показывалась
    /// живой. Показывать такие числа нельзя — лучше честно отказаться от замера.
    /// </summary>
    /// <summary>
    /// Ищем интерфейс с НАШИМ адресом, а не по имени. На macOS utun0..utun3 подняты почти
    /// всегда (iCloud, Handoff), и проверка по имени навсегда запретила бы замер — тупик
    /// на ровном месте.
    /// </summary>
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

        return measured
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
}
