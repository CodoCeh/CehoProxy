using System.Text.Json.Nodes;

namespace ProxyCage.Core.Tests;

/// <summary>
/// hysteria2 разбирается и собирается иначе, чем остальные: он поверх UDP, пароль
/// лежит не там, где у vless, и есть обфускация. Здесь закреплено то, что проверено
/// живым трафиком через настоящую ноду.
/// </summary>
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
        // поверх UDP и всегда с шифрованием — иначе движок не примет
        Assert.Equal("quic", n.Network);
        Assert.Equal("tls", n.Security);
    }

    [Theory]
    [InlineData("insecure=1")]
    [InlineData("allowInsecure=1")]
    public void Understands_both_names_of_the_self_signed_flag(string param)
    {
        // у ноды владельца сертификат самоподписанный, и без этого флага соединения нет
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
        // пароль hysteria2 лежит в своём поле, а не там, где uuid у vless
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
        // «detour к пустому direct» движок не принимает и вовсе не стартует.
        // Поймано живьём: продукт молча оставался без туннеля
        var nodes = SubscriptionParser.Parse("hy2://secret@198.51.100.7:443#Нода");
        var cfg = new CehoConfig { Apps = { new AppEntry { Name = "проба", Folder = "/opt/proba" } } };

        var dns = JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(nodes, cfg))!["dns"]!;
        foreach (var server in dns["servers"]!.AsArray())
        {
            if ((string?)server!["tag"] == "dns-proxy") continue;   // этот как раз через туннель
            Assert.Null(server["detour"]);
        }
    }

    [Fact]
    public void Never_drops_hysteria2_by_speed()
    {
        // задержку у него не измерить: он поверх UDP, а проба идёт TCP-рукопожатием
        var node = Parse("hy2://secret@198.51.100.7:443#Нода");
        var cfg = new CehoConfig { MaxLatencyMs = 1 };
        Assert.False(SingBoxConfigGenerator.IsTooSlow(node, cfg));
    }
}
