using System.Net;
using System.Text.Json.Nodes;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public sealed class NodeCountryServiceTests
{
    [Fact]
    public void Single_node_probe_uses_a_local_mixed_inbound_and_no_tun()
    {
        var node = new ProxyNode
        {
            Tag = "n001", Protocol = ProxyProtocol.Vless, Server = "edge.example", Port = 443,
            Credential = "test", Remark = "Germany DE", CountryCode = "DE",
        };

        var config = JsonNode.Parse(SingBoxNodeExitIpProbe.BuildConfig(node, 24567))!;

        Assert.DoesNotContain(config["inbounds"]!.AsArray(), inbound => (string?)inbound!["type"] == "tun");
        Assert.Equal(24567, (int?)config["inbounds"]![0]!["listen_port"]);
        Assert.Equal("n001", (string?)config["route"]!["final"]);
        Assert.Equal(2, config["outbounds"]!.AsArray().Count);
        Assert.Equal("vless", (string?)config["outbounds"]![0]!["type"]);
        Assert.Null(config["dns"]!["servers"]![0]!["detour"]);
    }

    [Fact]
    public async Task Verified_exit_ip_overrides_country_guessed_from_node_remark()
    {
        var temp = Path.Combine(Path.GetTempPath(), "cehoproxy-country-" + Guid.NewGuid().ToString("N"));
        try
        {
            var node = new ProxyNode
            {
                Tag = "n001", Protocol = ProxyProtocol.Vless, Server = "edge.example", Port = 443,
                Credential = "test", Remark = "Germany DE",
                CountryCode = CountryResolver.ResolveCode("Germany DE"),
            };
            var probe = new FixedExitIpProbe(IPAddress.Parse("198.51.100.10"));
            var service = new NodeCountryService(probe, new FixedCountryLookup("US"),
                Path.Combine(temp, "cache.json"), "db-2026-09");

            var rows = await service.RefreshAsync(new[] { node });

            Assert.Equal("US", node.CountryCode);
            Assert.Equal("США", node.CountryName);
            Assert.Single(rows);
            Assert.Equal("US", rows[0].Code);
            Assert.Equal(1, probe.Calls);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Failed_exit_probe_keeps_country_unknown_instead_of_trusting_remark()
    {
        var temp = Path.Combine(Path.GetTempPath(), "cehoproxy-country-" + Guid.NewGuid().ToString("N"));
        try
        {
            var node = new ProxyNode
            {
                Tag = "n001", Protocol = ProxyProtocol.Vless, Server = "edge.example", Port = 443,
                Credential = "test", Remark = "Germany DE", CountryCode = "DE",
            };
            var service = new NodeCountryService(new FixedExitIpProbe(null), new FixedCountryLookup("US"),
                Path.Combine(temp, "cache.json"), "db-2026-09");

            var rows = await service.RefreshAsync(new[] { node });

            Assert.Null(node.CountryCode);
            Assert.Single(rows);
            Assert.Equal(CountryResolver.Unknown, rows[0].Code);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    private sealed class FixedExitIpProbe(IPAddress? address) : INodeExitIpProbe
    {
        public int Calls { get; private set; }

        public Task<IPAddress?> ProbeAsync(ProxyNode node, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(address);
        }
    }

    private sealed class FixedCountryLookup(string? code) : INodeCountryLookup
    {
        public string? LookupCountry(IPAddress address) => code;
    }
}
