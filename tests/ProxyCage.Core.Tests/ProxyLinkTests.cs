using System.Text.Json.Nodes;
using ProxyCage.Core;
using Xunit;

namespace ProxyCage.Core.Tests;

public class ProxyLinkTests
{
    private const string Http = "http://user1_ab:pa55w0rd_country-SG,NG@proxy.example.net:1000";
    private const string Socks = "socks5://user1_ab:pa55w0rd_country-SG,NG@proxy.example.net:1002";

    [Fact]
    public void Http_link_becomes_an_http_node()
    {
        var node = Assert.Single(SubscriptionParser.Parse(Http));

        Assert.Equal(ProxyProtocol.Http, node.Protocol);
        Assert.Equal("proxy.example.net", node.Server);
        Assert.Equal(1000, node.Port);
        Assert.Equal("user1_ab", node.Credential);
        Assert.Equal("pa55w0rd_country-SG,NG", node.TuicPassword);
    }

    [Fact]
    public void Socks_link_becomes_a_socks_node()
    {
        var node = Assert.Single(SubscriptionParser.Parse(Socks));

        Assert.Equal(ProxyProtocol.Socks, node.Protocol);
        Assert.Equal(1002, node.Port);
        Assert.Equal("user1_ab", node.Credential);
    }

    [Fact]
    public void Name_after_hash_is_used_as_remark()
    {
        var node = Assert.Single(SubscriptionParser.Parse(Socks + "#Singapore%201"));

        Assert.Equal("Singapore 1", node.Remark);
    }

    [Fact]
    public void Percent_encoded_credentials_are_decoded()
    {
        var node = Assert.Single(SubscriptionParser.Parse("socks5://me%40mail:p%3Aw@10.1.2.3:1080"));

        Assert.Equal("me@mail", node.Credential);
        Assert.Equal("p:w", node.TuicPassword);
    }

    [Fact]
    public void Link_without_credentials_is_accepted()
    {
        var node = Assert.Single(SubscriptionParser.Parse("socks5://10.1.2.3:1080"));

        Assert.Equal("", node.Credential);
        Assert.Equal("10.1.2.3:1080", node.Remark);
    }

    [Theory]
    [InlineData("http://user:pw@proxy.example.net:1000", true)]
    [InlineData("http://proxy.example.net:3128", true)]
    [InlineData("http://proxy.example.net:3128/", true)]
    [InlineData("https://sub.example.com/sub/TOKEN", false)]
    [InlineData("http://sub.example.com/sub/TOKEN", false)]
    [InlineData("http://sub.example.com:8080/sub/TOKEN", false)]
    [InlineData("http://sub.example.com", false)]
    public void Only_a_bare_host_and_port_is_a_proxy_link(string text, bool proxy)
    {
        Assert.Equal(proxy, SubscriptionParser.IsHttpProxyUri(text));
        Assert.Equal(proxy, SubscriptionParser.LooksLikeNodeUri(text));
    }

    [Fact]
    public void Http_proxy_link_is_no_longer_taken_for_naive() =>
        Assert.False(NaiveProxyHelper.IsNaiveUri(Http));

    [Fact]
    public void Http_outbound_carries_credentials()
    {
        var o = OutboundBuilder.Build(Assert.Single(SubscriptionParser.Parse(Http)));

        Assert.Equal("http", (string?)o["type"]);
        Assert.Equal("proxy.example.net", (string?)o["server"]);
        Assert.Equal(1000, (int?)o["server_port"]);
        Assert.Equal("user1_ab", (string?)o["username"]);
        Assert.Equal("pa55w0rd_country-SG,NG", (string?)o["password"]);
    }

    [Fact]
    public void Socks_outbound_is_version_five()
    {
        var o = OutboundBuilder.Build(Assert.Single(SubscriptionParser.Parse(Socks)));

        Assert.Equal("socks", (string?)o["type"]);
        Assert.Equal("5", (string?)o["version"]);
        Assert.Equal(1002, (int?)o["server_port"]);
    }

    [Fact]
    public void Outbound_without_credentials_has_no_login()
    {
        var o = OutboundBuilder.Build(Assert.Single(SubscriptionParser.Parse("socks5://10.1.2.3:1080")));

        Assert.Null(o["username"]);
        Assert.Null(o["password"]);
    }

    [Fact]
    public void Shown_link_does_not_reveal_the_password()
    {
        var shown = WebServer.MaskUrl(Http);

        Assert.DoesNotContain("pa55w0rd", shown);
        Assert.DoesNotContain("user1_ab", shown);
        Assert.Contains("proxy.example.net:1000", shown);
    }

    [Fact]
    public void A_proxy_link_is_an_acceptable_subscription_source()
    {
        Assert.True(SubscriptionParser.IsAcceptableSource(Http));
        Assert.True(SubscriptionParser.IsAcceptableSource(Socks));
    }
}
