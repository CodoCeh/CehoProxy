namespace ProxyCage.Core.Tests;

/// <summary>
/// Подписку выдают не только списком ссылок. Здесь проверяется, что продукт принимает
/// и готовые конфигурации: человек вставляет то, что ему дали, а формат — наша забота.
/// </summary>
public class SubscriptionFormatsTests
{
    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    [Fact]
    public void Reads_xray_json_with_one_config_per_node()
    {
        var nodes = SubscriptionParser.Parse(Fixture("sub-xray.json"));
        Assert.Equal(3, nodes.Count);

        // имя ноды у xray лежит на уровне конфига, а не outbound
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

        // «Автовыбор» и здесь остаётся служебной записью
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
    public void Reads_clash_yaml_in_both_styles()
    {
        var nodes = SubscriptionParser.Parse(Fixture("sub-clash.yaml"));
        Assert.Equal(3, nodes.Count);

        // однострочная запись
        var pl = nodes.First(n => n.CountryCode == "PL");
        Assert.Equal(ProxyProtocol.Vless, pl.Protocol);
        Assert.Equal(8443, pl.Port);
        Assert.Equal("grpc", pl.Network);
        Assert.Equal("reality", pl.Security);
        Assert.Equal("GunService", pl.ServiceName);

        // многострочная запись
        var us = nodes.First(n => n.CountryCode == "US");
        Assert.Equal(ProxyProtocol.Trojan, us.Protocol);
        Assert.Equal("trojan-secret", us.Credential);
        Assert.Equal("example.com", us.Sni);

        var sg = nodes.First(n => n.CountryCode == "SG");
        Assert.Equal(ProxyProtocol.Shadowsocks, sg.Protocol);
        Assert.Equal("chacha20-ietf-poly1305", sg.Method);

        // раздел proxy-groups нодами не является
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
        // провайдеры заворачивают в base64 что угодно, а не только список ссылок
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
    public void Unknown_text_is_not_mistaken_for_a_subscription()
    {
        Assert.Empty(SubscriptionParser.Parse("<html><body>404</body></html>"));
        Assert.Empty(SubscriptionParser.Parse("{\"error\":\"not found\"}"));
        Assert.Empty(SubscriptionParser.Parse(""));
    }
}
