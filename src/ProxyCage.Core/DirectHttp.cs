using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace ProxyCage.Core;

/// <summary>
/// HTTP-клиент в обход TUN: резолв через DirectDns, TCP с физического интерфейса.
/// Нужен для GitHub API и скачивания релизов, когда защита ломает системный DNS.
/// </summary>
public static class DirectHttp
{
    public static HttpClient CreateClient(TimeSpan timeout, string? tunAddress = null)
    {
        var tun = tunAddress ?? TunAddressForBypass();
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectCallback = (ctx, ct) => ConnectAsync(ctx, ct, tun),
        };
        var client = new HttpClient(handler) { Timeout = timeout };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "CehoProxy");
        return client;
    }

    private static string TunAddressForBypass()
    {
        var root = Environment.GetEnvironmentVariable("CEHOPROXY_HOME") ?? Os.DefaultRoot;
        try { return CehoConfig.Load(Path.Combine(root, "config.json")).TunAddress; }
        catch { return CehoConfig.DefaultTunAddress; }
    }

    private static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken, string tunAddress)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;
        var addresses = IPAddress.TryParse(host, out var literal)
            ? new[] { literal }
            : await DirectDnsResolver.ResolveAsync(host, cancellationToken, tunAddress);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        var bind = Os.PhysicalBindAddress(tunAddress);
        if (bind is not null)
            socket.Bind(new IPEndPoint(bind, 0));
        try
        {
            await socket.ConnectAsync(addresses, port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
