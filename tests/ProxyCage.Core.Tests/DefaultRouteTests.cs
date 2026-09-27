namespace ProxyCage.Core.Tests;

public class DefaultRouteTests
{
    [Fact]
    public void Mac_route_output_gives_the_interface()
    {
        const string output = """
               route to: default
            destination: default
                   mask: default
                gateway: 192.168.0.1
              interface: en0
                  flags: <UP,GATEWAY,DONE,STATIC,PRCLONING,GLOBAL>
            """;

        Assert.Equal("en0", Os.ParseDefaultRouteInterface(output, mac: true));
    }

    [Fact]
    public void Linux_route_output_gives_the_interface()
    {
        Assert.Equal("eth0", Os.ParseDefaultRouteInterface(
            "default via 192.168.0.1 dev eth0 proto dhcp src 192.168.0.42 metric 100", mac: false));
        Assert.Equal("wg0", Os.ParseDefaultRouteInterface(
            "default dev wg0 proto static metric 50", mac: false));
    }

    [Fact]
    public void No_route_gives_nothing()
    {
        Assert.Null(Os.ParseDefaultRouteInterface("", mac: true));
        Assert.Null(Os.ParseDefaultRouteInterface("RTNETLINK answers: Network is unreachable", mac: false));
    }

    [Fact]
    public void Split_exits_keep_ipv6_out_of_the_tunnel()
    {
        if (Os.IsWindows) return;
        var cfg = new CehoConfig { TunIpv6 = true };
        cfg.Apps.Add(new AppEntry { Name = "тест", Folder = "/opt/test-app", Enabled = true });
        try
        {
            SingBoxConfigGenerator.Ipv6Allowed = false;
            Assert.DoesNotContain(CehoConfig.TunAddress6, SingBoxConfigGenerator.GenerateFailClosed(cfg));

            SingBoxConfigGenerator.Ipv6Allowed = true;
            Assert.Contains(CehoConfig.GuardTunAddress6, SingBoxConfigGenerator.GenerateFailClosed(cfg));
        }
        finally { SingBoxConfigGenerator.Ipv6Allowed = true; }
    }
}
