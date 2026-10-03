using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class EngineConnectionsTests
{
    private const string Json = """
        {"connections":[
          {"chains":["direct"],"metadata":{"processPath":"/opt/app/bin/app"}},
          {"chains":["n001","proxy"],"metadata":{"processPath":"/opt/app/bin/app"}},
          {"chains":["n002","proxy"],"metadata":{"processPath":"/opt/app/bin/app"}},
          {"chains":["direct"],"metadata":{"processPath":"/usr/bin/other"}},
          {"chains":["direct"],"metadata":{"processPath":""}}
        ]}
        """;

    [Fact]
    public void Counts_vpn_and_direct_connections_of_the_app()
    {
        var connections = EngineConnections.Parse(Json);
        Assert.Equal(4, connections.Count);

        var (vpn, direct) = EngineConnections.CountFor(new AppEntry { Name = "app", Folder = "/opt/app" }, connections);
        Assert.Equal((2, 1), (vpn, direct));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("[42]")]
    [InlineData("[\"unrecognized\"]")]
    [InlineData("[\"block\",\"proxy\"]")]
    [InlineData("[\"dns-out\"]")]
    public void Unknown_or_nontraffic_chains_do_not_become_vpn_evidence(string chains)
    {
        var json = "{\"connections\":[{\"chains\":" + chains + ",\"metadata\":{\"processPath\":\"/opt/app/bin/app\"}}]}";
        Assert.Empty(EngineConnections.Parse(json));
    }

    [Theory]
    [InlineData("proxy")]
    [InlineData("n001")]
    [InlineData("proxy-app-2")]
    [InlineData("proxy-site-nl")]
    public void Generated_route_tags_remain_valid_vpn_evidence(string tag)
    {
        var json = "{\"connections\":[{\"chains\":[\"" + tag + "\"],\"metadata\":{\"processPath\":\"/opt/app/bin/app\"}}]}";
        Assert.False(Assert.Single(EngineConnections.Parse(json)).Direct);
    }

    [Theory]
    [InlineData("192.168.0.1:53", true)]
    [InlineData("10.1.2.3:443", true)]
    [InlineData("172.20.0.5:80", true)]
    [InlineData("169.254.1.1:80", true)]
    [InlineData("100.84.177.25:2222", true)]
    [InlineData("[fe80::1%12]:443", true)]
    [InlineData("[::1]:80", true)]
    [InlineData("239.255.255.250:1900", true)]
    [InlineData("149.154.167.41:443", false)]
    [InlineData("172.32.0.1:443", false)]
    [InlineData("[2001:4860:4860::8888]:443", false)]
    public void Home_network_is_not_a_leak(string endpoint, bool isPrivate) =>
        Assert.Equal(isPrivate, ProcessInspector.IsPrivateEndpoint(endpoint));
}
