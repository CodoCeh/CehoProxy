using System.Net;
using System.Net.Sockets;

namespace ProxyCage.Core;

/// <summary>
/// Готовит ноду к dial в sing-box: резолвит server-домен заранее через DirectDns,
/// чтобы urltest не зависел от dns-direct под TUN (Windows ломает системный DNS).
/// SNI для REALITY/TLS сохраняется исходным доменом.
/// </summary>
public static class NodeDialPreparer
{
    public static ProxyNode Prepare(ProxyNode node, string? tunAddress = null)
    {
        if (IPAddress.TryParse(node.Server, out _)) return node;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var addresses = DirectDnsResolver
                .ResolveAsync(node.Server, cts.Token, tunAddress)
                .GetAwaiter().GetResult();
            if (addresses.Length == 0) return node;

            var ip = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                     ?? addresses[0];

            return new ProxyNode
            {
                Tag = node.Tag,
                Protocol = node.Protocol,
                Server = ip.ToString(),
                Port = node.Port,
                Credential = node.Credential,
                Network = node.Network,
                Security = node.Security,
                Flow = node.Flow,
                Sni = node.Sni ?? node.Server,
                Fingerprint = node.Fingerprint,
                PublicKey = node.PublicKey,
                ShortId = node.ShortId,
                AllowInsecure = node.AllowInsecure,
                Alpn = node.Alpn,
                ServiceName = node.ServiceName,
                Path = node.Path,
                Host = node.Host,
                Method = node.Method,
                AlterId = node.AlterId,
                ObfsPassword = node.ObfsPassword,
                CongestionControl = node.CongestionControl,
                TuicPassword = node.TuicPassword,
                Remark = node.Remark,
                CountryCode = node.CountryCode,
                CountryName = node.CountryName,
                Source = node.Source,
                IsMeta = node.IsMeta,
            };
        }
        catch
        {
            return node;
        }
    }
}
