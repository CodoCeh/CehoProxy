namespace ProxyCage.Core.Tests;

public class SubscriptionFormatsTests
{
    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    [Fact]
    public void Reads_xray_json_with_one_config_per_node()
    {
        var nodes = SubscriptionParser.Parse(Fixture("sub-xray.json"));
        Assert.Equal(3, nodes.Count);

        var de = nodes.First(n => n.CountryCode == "DE");
        Assert.Equal(ProxyProtocol.Vless, de.Protocol);
        Assert.Equal("node1.example.com", de.Server);
        Assert.Equal(8444, de.Port);
        Assert.Equal("grpc", de.Network);
        Assert.Equal("reality", de.Security);
        Assert.Equal("GunService", de.ServiceName);
        Assert.Equal("example.com", de.Sni);
        Assert.False(string.IsNullOrEmpty(de.PublicKey));

        var nl = nodes.First(n => n.CountryCode == "NL");
        Assert.Equal(ProxyProtocol.Vmess, nl.Protocol);
        Assert.Equal("ws", nl.Network);
        Assert.Equal("/ws", nl.Path);
        Assert.Equal("cdn.example.com", nl.Host);

        Assert.Single(nodes.Where(n => n.IsMeta));
    }

    [Fact]
    public void Reads_singbox_json()
    {
        var nodes = SubscriptionParser.Parse(Fixture("sub-singbox.json"));
        Assert.Equal(2, nodes.Count);

        var fi = nodes.First(n => n.CountryCode == "FI");
        Assert.Equal(ProxyProtocol.Vless, fi.Protocol);
        Assert.Equal("reality", fi.Security);
        Assert.Equal("xtls-rprx-vision", fi.Flow);
        Assert.Equal("chrome", fi.Fingerprint);

        var tr = nodes.First(n => n.CountryCode == "TR");
        Assert.Equal(ProxyProtocol.Hysteria2, tr.Protocol);
        Assert.Equal("hy2-secret", tr.TuicPassword);
        Assert.Equal("obfs-secret", tr.ObfsPassword);
        Assert.True(tr.AllowInsecure);
    }

    [Fact]
    public void Reads_xray_hysteria2_as_a_single_config()
    {
        var nodes = SubscriptionParser.Parse(Fixture("sub-xray-hysteria2.json"));
        var hy2 = Assert.Single(nodes.Where(n => !n.IsMeta));
        Assert.Equal(ProxyProtocol.Hysteria2, hy2.Protocol);
        Assert.Equal("hy2.example.com", hy2.Server);
        Assert.Equal(8443, hy2.Port);
        Assert.Equal("hy2-auth-secret", hy2.Credential);
        Assert.Equal("www.example.com", hy2.Sni);
        Assert.Equal("quic", hy2.Network);
        Assert.Equal("tls", hy2.Security);
        Assert.True(hy2.AllowInsecure);
        Assert.Equal("US", hy2.CountryCode);
    }

    [Fact]
    public void Reads_clash_yaml_in_both_styles()
    {
        var nodes = SubscriptionParser.Parse(Fixture("sub-clash.yaml"));
        Assert.Equal(3, nodes.Count);

        var pl = nodes.First(n => n.CountryCode == "PL");
        Assert.Equal(ProxyProtocol.Vless, pl.Protocol);
        Assert.Equal(8443, pl.Port);
        Assert.Equal("grpc", pl.Network);
        Assert.Equal("reality", pl.Security);
        Assert.Equal("GunService", pl.ServiceName);

        var us = nodes.First(n => n.CountryCode == "US");
        Assert.Equal(ProxyProtocol.Trojan, us.Protocol);
        Assert.Equal("trojan-secret", us.Credential);
        Assert.Equal("example.com", us.Sni);

        var sg = nodes.First(n => n.CountryCode == "SG");
        Assert.Equal(ProxyProtocol.Shadowsocks, sg.Protocol);
        Assert.Equal("chacha20-ietf-poly1305", sg.Method);

        Assert.DoesNotContain(nodes, n => n.Remark == "auto");
    }

    [Fact]
    public void Reads_sip008()
    {
        var nodes = SubscriptionParser.Parse(Fixture("sub-sip008.json"));
        var jp = Assert.Single(nodes);
        Assert.Equal(ProxyProtocol.Shadowsocks, jp.Protocol);
        Assert.Equal("aes-256-gcm", jp.Method);
        Assert.Equal("JP", jp.CountryCode);
    }

    [Theory]
    [InlineData("sub-xray.json")]
    [InlineData("sub-singbox.json")]
    [InlineData("sub-clash.yaml")]
    [InlineData("sub-sip008.json")]
    public void Every_format_survives_base64_wrapping(string fixture)
    {
        var raw = Fixture(fixture);
        var packed = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw));
        Assert.Equal(SubscriptionParser.Parse(raw).Count, SubscriptionParser.Parse(packed).Count);
    }

    [Theory]
    [InlineData("sub-xray.json")]
    [InlineData("sub-singbox.json")]
    [InlineData("sub-clash.yaml")]
    [InlineData("sub-sip008.json")]
    public void Every_format_produces_a_config_the_engine_can_read(string fixture)
    {
        var nodes = SubscriptionParser.Parse(Fixture(fixture)).Where(n => !n.IsMeta).ToList();
        var json = SingBoxConfigGenerator.Generate(nodes, new ProxyCageSettings
        {
            FolderPath = "/opt/app",
            ExcludedExitCountries = new(StringComparer.OrdinalIgnoreCase),
            RuleSetDir = "rulesets",
        });

        var outbounds = System.Text.Json.Nodes.JsonNode.Parse(json)!["outbounds"]!.AsArray();
        var pool = outbounds.First(o => (string?)o!["tag"] == "proxy")!["outbounds"]!.AsArray();
        Assert.Equal(nodes.Count, pool.Count);
    }

    [Fact]
    public void Reads_singbox_naive_outbound_with_basic_auth()
    {
        var nodes = SubscriptionParser.Parse(Fixture("sub-caddy-naive.json"));
        var node = Assert.Single(nodes);

        Assert.Equal(ProxyProtocol.Naive, node.Protocol);
        Assert.Equal("site.roomspace.team", node.Server);
        Assert.Equal(8443, node.Port);
        Assert.Equal("bsv", node.Credential);
        Assert.Equal("REDACTED_TEST_PASSWORD", node.TuicPassword);
        Assert.Equal("site.roomspace.team", node.Sni);
        Assert.Equal("Caddy BSV", node.Remark);

        var outbound = OutboundBuilder.Build(node);
        Assert.Equal("naive", outbound["type"]!.GetValue<string>());
        Assert.Equal("bsv", outbound["username"]!.GetValue<string>());
        Assert.Equal("REDACTED_TEST_PASSWORD", outbound["password"]!.GetValue<string>());
        Assert.Equal("site.roomspace.team", outbound["tls"]!["server_name"]!.GetValue<string>());
    }

    [Fact]
    public void Reads_singbox_naive_outbound_array_without_wrapper()
    {
        const string json = """
            [
              {
                "type": "naive",
                "tag": "Caddy BSV",
                "server": "185.76.13.167",
                "server_port": 8443,
                "username": "bsv",
                "password": "REDACTED_TEST_PASSWORD",
                "tls": { "enabled": true, "server_name": "site.roomspace.team" }
              }
            ]
            """;

        var node = Assert.Single(SubscriptionParser.Parse(json));
        Assert.Equal(ProxyProtocol.Naive, node.Protocol);
        Assert.Equal("185.76.13.167", node.Server);
        Assert.Equal("site.roomspace.team", node.Sni);
        Assert.Equal("bsv", node.Credential);
    }

    [Fact]
    public void Reads_clash_naive_proxy()
    {
        var node = Assert.Single(SubscriptionParser.Parse(Fixture("sub-caddy-naive-clash.yaml")));
        Assert.Equal(ProxyProtocol.Naive, node.Protocol);
        Assert.Equal("bsv", node.Credential);
        Assert.Equal("REDACTED_TEST_PASSWORD", node.TuicPassword);
        Assert.Equal("site.roomspace.team", node.Sni);
    }

    [Fact]
    public void Reads_base64_caddy_naive_subscription()
    {
        var node = Assert.Single(SubscriptionParser.Parse(Fixture("sub-caddy-naive.b64.txt")));
        Assert.Equal(ProxyProtocol.Naive, node.Protocol);
        Assert.Equal("site.roomspace.team", node.Server);
        Assert.Equal("bsv", node.Credential);
        Assert.Equal("REDACTED_TEST_PASSWORD", node.TuicPassword);
    }

    [Fact]
    public void Reads_naive_plus_https_share_uri()
    {
        var node = Assert.Single(SubscriptionParser.Parse(
            "naive+https://bsv:REDACTED_TEST_PASSWORD@site.roomspace.team:8443?sni=site.roomspace.team#Caddy%20BSV\n"));
        Assert.Equal(ProxyProtocol.Naive, node.Protocol);
        Assert.Equal("bsv", node.Credential);
        Assert.Equal("site.roomspace.team", node.Sni);
    }

    [Fact]
    public void Unknown_text_is_not_mistaken_for_a_subscription()
    {
        Assert.Empty(SubscriptionParser.Parse("<html><body>404</body></html>"));
        Assert.Empty(SubscriptionParser.Parse("{\"error\":\"not found\"}"));
        Assert.Empty(SubscriptionParser.Parse(""));
    }
}
