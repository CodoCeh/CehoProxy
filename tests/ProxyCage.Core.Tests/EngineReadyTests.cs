using System.Net;
using System.Net.Sockets;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public sealed class EngineReadyTests
{
    [Fact]
    public async Task Engine_counts_as_up_only_when_every_port_answers()
    {
        var open = new TcpListener(IPAddress.Loopback, 0);
        open.Start();
        var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var openPort = ((IPEndPoint)open.LocalEndpoint).Port;
        var closedPort = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        try
        {
            Assert.True(await SingBoxProcess.ListensAsync(openPort));
            Assert.False(await SingBoxProcess.ListensAsync(openPort, closedPort));
        }
        finally { open.Stop(); }
    }
}
