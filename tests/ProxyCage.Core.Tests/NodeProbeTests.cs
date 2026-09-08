using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class NodeProbeTests
{
    private static ProxyNode Node(string tag, string server, string? country = "NL") => new()
    {
        Tag = tag,
        Protocol = ProxyProtocol.Vless,
        Server = server,
        Port = 443,
        Credential = "x",
        Network = "tcp",
        Security = "none",
        CountryCode = country,
    };

    [Fact]
    public void Live_urltest_delay_overrides_saved_latency()
    {
        var node = Node("n01", "fast.example.com");
        var cfg = new CehoConfig { NodeLatency = { [node.Key] = 400 } };
        var live = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [node.Tag] = 120 };

        Assert.Equal(120, NodeProbe.LatencyFor(node, cfg, live));
    }

    [Fact]
    public void Summarize_builds_country_rows_from_saved_latency()
    {
        var fast = Node("n01", "a.example.com", "FI");
        var slow = Node("n02", "b.example.com", "FI");
        var cfg = new CehoConfig
        {
            NodeLatency =
            {
                [fast.Key] = 40,
                [slow.Key] = 900,
            },
        };

        var rows = NodeProbe.Summarize(new[] { fast, slow }, cfg);
        var fi = Assert.Single(rows);

        Assert.Equal("FI", fi.Code);
        Assert.Equal(2, fi.Nodes);
        Assert.Equal(2, fi.Alive);
        Assert.Equal(40, fi.BestMs);
    }

    [Fact]
    public void Measure_blocked_only_when_engine_and_tunnel_are_up()
    {
        Assert.False(NodeProbe.MeasureBlocked(CehoConfig.DefaultTunAddress, engineRunning: false));
        Assert.False(NodeProbe.MeasureBlocked(CehoConfig.DefaultTunAddress, engineRunning: true));
    }
}
