using System.Net;
using System.Net.Sockets;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class ProxyPortTests
{
    [Fact]
    public void Busy_proxy_port_moves_to_a_free_one_and_is_saved()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-port-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "config.json");
        try
        {
            using var squatter = new TcpListener(IPAddress.Loopback, 0);
            squatter.Start();
            var busy = ((IPEndPoint)squatter.LocalEndpoint).Port;

            var cfg = new CehoConfig { MixedPort = busy, WebPort = 8899 };
            cfg.Save(path);

            var note = Preflight.SaveProxyPortIfBusy(cfg, path);

            var saved = CehoConfig.Load(path);
            Assert.NotEqual(busy, saved.MixedPort);
            Assert.NotNull(note);
            Assert.Contains(busy.ToString(), note);
            Assert.Contains(saved.MixedPort.ToString(), note);
            Assert.NotEqual(true, Preflight.TcpPortTaken(saved.MixedPort));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void Free_proxy_port_stays()
    {
        using var squatter = new TcpListener(IPAddress.Loopback, 0);
        squatter.Start();
        var port = ((IPEndPoint)squatter.LocalEndpoint).Port;
        squatter.Stop();

        var cfg = new CehoConfig { MixedPort = port };
        Assert.False(Preflight.TryMoveProxyPortIfBusy(cfg, out _, out _));
        Assert.Equal(port, cfg.MixedPort);
    }

    [Fact]
    public void Move_skips_the_panel_port()
    {
        using var squatter = new TcpListener(IPAddress.Loopback, 0);
        squatter.Start();
        var busy = ((IPEndPoint)squatter.LocalEndpoint).Port;
        var panel = Preflight.NextFreePort(busy);

        var cfg = new CehoConfig { MixedPort = busy, WebPort = panel };
        Assert.True(Preflight.TryMoveProxyPortIfBusy(cfg, out _, out var to));
        Assert.NotEqual(busy, to);
        Assert.NotEqual(panel, to);
    }
}
