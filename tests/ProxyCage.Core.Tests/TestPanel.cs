using System.Net;
using System.Net.Sockets;

namespace ProxyCage.Core.Tests;

internal static class TestPanel
{
    private const int Attempts = 20;

    public static int Start(WebServer web)
    {
        for (var attempt = 1; ; attempt++)
        {
            var port = FreePort();
            try
            {
                web.Start(port);
                return port;
            }
            catch (HttpListenerException) when (attempt < Attempts) { }
        }
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
