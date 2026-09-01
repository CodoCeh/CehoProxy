namespace ProxyCage.Core.Tests;

public class SpeedFilterTests
{
    private static ProxyNode Node(string server, ProxyProtocol protocol = ProxyProtocol.Vless,
        string? country = "NL") => new()
    {
        Tag = "n01",
        Protocol = protocol,
        Server = server,
        Port = 443,
        Credential = "x",
        Network = "tcp",
        Security = "none",
        CountryCode = country,
    };

    [Fact]
    public void Drops_only_nodes_measured_slower_than_the_limit()
    {
        var fast = Node("fast.example.com");
        var slow = Node("slow.example.com");
        var cfg = new CehoConfig
        {
            MaxLatencyMs = 100,
            NodeLatency = { [fast.Key] = 40, [slow.Key] = 400 },
        };

        Assert.False(SingBoxConfigGenerator.IsTooSlow(fast, cfg));
        Assert.True(SingBoxConfigGenerator.IsTooSlow(slow, cfg));
    }

    [Fact]
    public void Keeps_nodes_that_were_never_measured()
    {
        var udp = Node("hy2.example.com", ProxyProtocol.Hysteria2);
        var cfg = new CehoConfig { MaxLatencyMs = 50 };
        Assert.False(SingBoxConfigGenerator.IsTooSlow(udp, cfg));
    }

    [Fact]
    public void Keeps_everything_when_the_filter_is_off()
    {
        var node = Node("slow.example.com");
        var cfg = new CehoConfig { MaxLatencyMs = null, NodeLatency = { [node.Key] = 5000 } };
        Assert.False(SingBoxConfigGenerator.IsTooSlow(node, cfg));
    }

    [Fact]
    public void Rejects_a_measurement_taken_from_under_another_tunnel()
    {
        var measured = new List<NodeProbe.Measured>
        {
            new(Node("a.example.com", country: "FI"), 0),
            new(Node("b.example.com", country: "NL"), 4),
            new(Node("c.example.com", country: "RU"), 5),
            new(Node("d.example.com", country: "DE"), 6),
        };
        Assert.True(NodeProbe.LooksLikeLocalAccept(measured));
    }

    [Fact]
    public void Accepts_an_honest_measurement()
    {
        var measured = new List<NodeProbe.Measured>
        {
            new(Node("a.example.com", country: "FI"), 38),
            new(Node("b.example.com", country: "NL"), 52),
            new(Node("c.example.com", country: "RU"), 12),
            new(Node("d.example.com", country: "DE"), 47),
        };
        Assert.False(NodeProbe.LooksLikeLocalAccept(measured));
    }

    [Fact]
    public void Does_not_suspect_a_provider_whose_nodes_are_all_nearby()
    {
        var measured = new List<NodeProbe.Measured>
        {
            new(Node("a.example.com", country: "RU"), 3),
            new(Node("b.example.com", country: "RU"), 4),
            new(Node("c.example.com", country: "RU"), 2),
        };
        Assert.False(NodeProbe.LooksLikeLocalAccept(measured));
    }
}
