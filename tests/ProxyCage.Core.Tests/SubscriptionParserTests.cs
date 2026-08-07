using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class SubscriptionParserTests
{
    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    private static string Vless() => Fixture("sub-example.txt");

    [Fact]
    public void Parses_all_vless_nodes()
    {
        var nodes = SubscriptionParser.Parse(Vless());
        Assert.Equal(14, nodes.Count);
    }

    [Fact]
    public void Resolves_countries_from_flag_emoji()
    {
        var nodes = SubscriptionParser.Parse(Vless());
        Assert.Equal(5, nodes.Count(n => n.CountryCode == "NL"));
        Assert.Single(nodes.Where(n => n.CountryCode == "RU"));
        Assert.Contains(nodes, n => n.CountryCode == "DE");
        Assert.Contains(nodes, n => n.CountryCode == "US");
        Assert.Contains(nodes, n => n.CountryCode == "TR");
    }

    [Fact]
    public void Marks_only_aggregate_entries_as_meta()
    {
        var nodes = SubscriptionParser.Parse(Vless());
        // служебная запись здесь одна — «Автовыбор»: это та же нода под другим именем.
        // «Ключ для роутера» служебной записью НЕ является: это обычная рабочая нода,
        // и выкидывать её из пула не за что
        Assert.Equal(1, nodes.Count(n => n.IsMeta));
        Assert.Equal(13, nodes.Count(n => !n.IsMeta));
    }

    [Fact]
    public void Keeps_nodes_whose_country_is_unknown()
    {
        var nodes = SubscriptionParser.Parse(Vless());
        var unknown = nodes.Where(n => n.CountryCode is null && !n.IsMeta).ToList();
        // нераспознанная страна — не повод выбрасывать ноду: раньше подписка
        // с непривычными подписями теряла из-за этого весь пул
        Assert.NotEmpty(unknown);
    }

    [Fact]
    public void Parses_reality_transport_details()
    {
        var nodes = SubscriptionParser.Parse(Vless());
        var real = nodes.First(n => n.CountryCode == "DE");
        Assert.Equal("reality", real.Security);
        Assert.False(string.IsNullOrEmpty(real.PublicKey));
        Assert.False(string.IsNullOrEmpty(real.Credential));
    }

    [Fact]
    public void Handles_base64_wrapped_subscription()
    {
        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Vless()));
        Assert.Equal(14, SubscriptionParser.Parse(b64).Count);
    }

    [Fact]
    public void Parses_every_supported_protocol()
    {
        var nodes = SubscriptionParser.Parse(Fixture("sub-protocols.txt"));
        Assert.Equal(6, nodes.Count);
        foreach (var expected in Enum.GetValues<ProxyProtocol>())
            Assert.Contains(nodes, n => n.Protocol == expected);
    }

    [Fact]
    public void Tuic_splits_uuid_and_password()
    {
        // логин у tuic приходит одной строкой uuid:password, и без разделения
        // sing-box падает на «invalid uuid» — ловили живьём
        var tuic = SubscriptionParser.Parse(Fixture("sub-protocols.txt"))
            .First(n => n.Protocol == ProxyProtocol.Tuic);
        Assert.Equal("00000000-0000-4000-8000-000000000002", tuic.Credential);
        Assert.Equal("tuic-password", tuic.TuicPassword);
    }

    [Fact]
    public void Shadowsocks_splits_method_and_password()
    {
        var ss = SubscriptionParser.Parse(Fixture("sub-protocols.txt"))
            .First(n => n.Protocol == ProxyProtocol.Shadowsocks);
        Assert.Equal("aes-256-gcm", ss.Method);
        Assert.Equal("ss-password", ss.Credential);
    }

    [Fact]
    public void Hysteria2_keeps_obfs_password()
    {
        var hy2 = SubscriptionParser.Parse(Fixture("sub-protocols.txt"))
            .First(n => n.Protocol == ProxyProtocol.Hysteria2);
        Assert.Equal("hy2-password", hy2.Credential);
        Assert.Equal("obfs-secret", hy2.ObfsPassword);
        Assert.True(hy2.AllowInsecure);
    }
}
