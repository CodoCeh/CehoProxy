using System.Net;
using System.Text.Json.Nodes;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class NodeDialPreparerTests
{
    [Fact]
    public void Prepare_resolves_domain_and_keeps_sni()
    {
        var node = new ProxyNode
        {
            Tag = "test",
            Server = "one.one.one.one",
            Port = 443,
            Security = "reality",
            PublicKey = "pk",
            ShortId = "sid",
        };

        var prepared = NodeDialPreparer.Prepare(node, CehoConfig.DefaultTunAddress);

        Assert.True(IPAddress.TryParse(prepared.Server, out _));
        Assert.Equal("one.one.one.one", prepared.Sni);
    }

    [Fact]
    public void Prepare_leaves_ip_server_unchanged()
    {
        var node = new ProxyNode { Tag = "ip", Server = "1.2.3.4", Port = 443 };
        var prepared = NodeDialPreparer.Prepare(node, CehoConfig.DefaultTunAddress);
        Assert.Equal("1.2.3.4", prepared.Server);
        Assert.Null(prepared.Sni);
    }

    [Fact]
    public void Engine_config_uses_public_dns_and_direct_bind()
    {
        var cfg = new CehoConfig
        {
            Apps = { new AppEntry { Name = "Test", Folder = @"C:\Apps\Test", Enabled = true } },
        };
        var nodes = new List<ProxyNode>
        {
            new()
            {
                Tag = "us-1",
                Server = "198.51.100.72",
                Port = 8444,
                Protocol = ProxyProtocol.Vless,
                Security = "reality",
                Credential = "00000000-0000-4000-8000-000000000001",
                Sni = "example.com",
                PublicKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                ShortId = "0123456789abcdef",
                CountryCode = "US",
            },
        };

        var json = SingBoxConfigGenerator.GenerateForConfig(nodes, cfg);
        var root = JsonNode.Parse(json)!;

        var direct = root["outbounds"]!.AsArray().First(o => (string?)o!["tag"] == "direct")!;
        var bind = (string?)direct["inet4_bind_address"];
        if (Os.PhysicalBindAddress(cfg.TunAddress) is not null)
            Assert.False(string.IsNullOrEmpty(bind));

        var dnsDirect = root["dns"]!["servers"]!.AsArray()
            .First(s => (string?)s!["tag"] == "dns-direct")!;
        Assert.Equal(Os.PublicResolver, (string?)dnsDirect["server"]);
    }
}
