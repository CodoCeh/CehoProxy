using System.Net;
using System.Text.Json.Nodes;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

/// <summary>
/// Регрессия v1.2.24: pre-resolve server в IP ломал REALITY/urltest под TUN.
/// </summary>
public class NodeDialPreparerTests
{
    private static CehoConfig MinimalCfg() => new()
    {
        Apps = { new AppEntry { Name = "Test", Folder = @"C:\Apps\Test", Enabled = true } },
    };

    private static ProxyNode RealityNode(string server, string? sni = null) => new()
    {
        Tag = "us-1",
        Server = server,
        Port = 8444,
        Protocol = ProxyProtocol.Vless,
        Security = "reality",
        Credential = "00000000-0000-4000-8000-000000000001",
        Sni = sni,
        PublicKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
        ShortId = "0123456789abcdef",
        CountryCode = "US",
    };

    [Fact]
    public void Engine_config_keeps_domain_server_for_urltest_not_preresolved_ip()
    {
        const string domain = "origin.example.com";
        var nodes = new List<ProxyNode> { RealityNode(domain, "www.bing.com") };
        var json = SingBoxConfigGenerator.GenerateForConfig(nodes, MinimalCfg());
        var outbound = JsonNode.Parse(json)!["outbounds"]!.AsArray()
            .First(o => (string?)o!["tag"] == "us-1")!;

        Assert.Equal(domain, (string?)outbound["server"]);
        Assert.Equal("www.bing.com", (string?)outbound["tls"]!["server_name"]);
        Assert.False(IPAddress.TryParse((string?)outbound["server"]!, out _));
    }

    [Fact]
    public void Engine_config_leaves_literal_ip_server_unchanged()
    {
        var nodes = new List<ProxyNode> { RealityNode("198.51.100.72", "example.com") };
        var json = SingBoxConfigGenerator.GenerateForConfig(nodes, MinimalCfg());
        var outbound = JsonNode.Parse(json)!["outbounds"]!.AsArray()
            .First(o => (string?)o!["tag"] == "us-1")!;

        Assert.Equal("198.51.100.72", (string?)outbound["server"]);
    }

    [Fact]
    public void Engine_config_uses_public_dns_direct_bind_on_dns_and_direct()
    {
        var nodes = new List<ProxyNode> { RealityNode("198.51.100.72", "example.com") };
        var cfg = MinimalCfg();
        var json = SingBoxConfigGenerator.GenerateForConfig(nodes, cfg);
        var root = JsonNode.Parse(json)!;

        var direct = root["outbounds"]!.AsArray().First(o => (string?)o!["tag"] == "direct")!;
        var dnsDirect = root["dns"]!["servers"]!.AsArray()
            .First(s => (string?)s!["tag"] == "dns-direct")!;

        Assert.Equal(Os.PublicResolver, (string?)dnsDirect["server"]);

        var bind = Os.PhysicalBindAddress(cfg.TunAddress)?.ToString();
        if (bind is null) return;

        Assert.Equal(bind, (string?)direct["inet4_bind_address"]);
        Assert.Equal(bind, (string?)dnsDirect["inet4_bind_address"]);
    }
}
