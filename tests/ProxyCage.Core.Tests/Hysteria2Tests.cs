using System.Text.Json.Nodes;

namespace ProxyCage.Core.Tests;

public class Hysteria2Tests
{
    private static ProxyNode Parse(string uri) => SubscriptionParser.Parse(uri).Single();

    [Theory]
    [InlineData("hy2://secret@198.51.100.7:443#Нода")]
    [InlineData("hysteria2://secret@198.51.100.7:443#Нода")]
    public void Reads_both_spellings_of_the_scheme(string uri)
    {
        var n = Parse(uri);
        Assert.Equal(ProxyProtocol.Hysteria2, n.Protocol);
        Assert.Equal("198.51.100.7", n.Server);
        Assert.Equal(443, n.Port);

        Assert.Equal("quic", n.Network);
        Assert.Equal("tls", n.Security);
    }

    [Theory]
    [InlineData("insecure=1")]
    [InlineData("allowInsecure=1")]
    public void Understands_both_names_of_the_self_signed_flag(string param)
    {
        Assert.True(Parse($"hy2://secret@198.51.100.7:443?{param}#Нода").AllowInsecure);
    }

    [Fact]
    public void Keeps_obfuscation_password()
    {
        var n = Parse("hy2://secret@198.51.100.7:443?obfs=salamander&obfs-password=obfsecret#Нода");
        Assert.Equal("obfsecret", n.ObfsPassword);
    }

    [Fact]
    public void Builds_an_outbound_the_engine_understands()
    {
        var nodes = SubscriptionParser.Parse(
            "hy2://secret@198.51.100.7:443?sni=www.bing.com&insecure=1&obfs=salamander&obfs-password=obfsecret#🇺🇸 Нода");
        var cfg = new CehoConfig { Apps = { new AppEntry { Name = "проба", Folder = "/opt/proba" } } };

        var root = JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(nodes, cfg))!;
        var node = root["outbounds"]!.AsArray().First(o => (string?)o!["type"] == "hysteria2")!;

        Assert.Equal("198.51.100.7", (string?)node["server"]);
        Assert.Equal(443, (int?)node["server_port"]);

        Assert.Equal("secret", (string?)node["password"]);
        Assert.Equal("salamander", (string?)node["obfs"]!["type"]);
        Assert.Equal("obfsecret", (string?)node["obfs"]!["password"]);
        Assert.True((bool?)node["tls"]!["enabled"]);
        Assert.Equal("www.bing.com", (string?)node["tls"]!["server_name"]);
        Assert.True((bool?)node["tls"]!["insecure"]);
    }

    [Fact]
    public void Direct_dns_server_has_no_detour()
    {
        var nodes = SubscriptionParser.Parse("hy2://secret@198.51.100.7:443#Нода");
        var cfg = new CehoConfig { Apps = { new AppEntry { Name = "проба", Folder = "/opt/proba" } } };

        var dns = JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(nodes, cfg))!["dns"]!;
        foreach (var server in dns["servers"]!.AsArray())
        {
            if ((string?)server!["tag"] == "dns-proxy") continue;
            Assert.Null(server["detour"]);
        }
    }

    [Fact]
    public void Never_drops_hysteria2_by_speed()
    {
        var node = Parse("hy2://secret@198.51.100.7:443#Нода");
        var cfg = new CehoConfig { MaxLatencyMs = 1 };
        Assert.False(SingBoxConfigGenerator.IsTooSlow(node, cfg));
    }
}
