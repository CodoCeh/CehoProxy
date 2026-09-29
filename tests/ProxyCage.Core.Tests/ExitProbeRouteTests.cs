using System.Text.Json.Nodes;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public sealed class ExitProbeRouteTests
{
    [Theory]
    [InlineData(CehoConfig.SiteModeOnly)]
    [InlineData(CehoConfig.SiteModeExcept)]
    public void Exit_check_goes_through_the_vpn_in_any_site_mode(string mode)
    {
        var nodes = SubscriptionParser.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "sub-example.txt")));
        var cfg = new CehoConfig
        {
            SiteMode = mode,
            Apps = { new AppEntry { Name = "app", Folder = "/tmp/ceho-probe-app" } },
            DirectSites = { SingBoxConfigGenerator.ExitProbeHost },
        };

        var rules = JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(nodes, cfg))!["route"]!["rules"]!.AsArray();
        var probe = rules.ToList().FindIndex(r =>
            r?["domain"] is JsonArray d && d.Any(x => (string?)x == SingBoxConfigGenerator.ExitProbeHost));
        var firstMixedElsewhere = rules.ToList().FindIndex(r =>
            r?["inbound"] is JsonArray i && i.Any(x => (string?)x == "mixed-in") && r["domain"] is null
            || (string?)r?["outbound"] == "direct" && r?["domain_suffix"] is JsonArray);

        Assert.True(probe > 0);
        Assert.Equal("proxy", (string?)rules[probe]!["outbound"]);
        Assert.True(firstMixedElsewhere < 0 || probe < firstMixedElsewhere);
    }
}
