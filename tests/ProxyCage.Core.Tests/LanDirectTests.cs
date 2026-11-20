using System.Text.Json.Nodes;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class LanDirectTests
{
    private static IReadOnlyList<ProxyNode> Nodes() => SubscriptionParser.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "sub-example.txt")));

    private static List<JsonObject> Rules(bool lan) => JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(Nodes(), new CehoConfig
    {
        Apps = { new AppEntry { Name = "Test", Folder = @"C:\Apps\Test", Enabled = true } },
        LanDirect = lan,
    }))!["route"]!["rules"]!.AsArray().Select(r => r!.AsObject()).ToList();

    [Fact]
    public void Lan_goes_direct_before_app_rules_by_default()
    {
        Assert.True(new CehoConfig().LanDirect);
        var rules = Rules(lan: true);
        var lan = rules.FindIndex(r => r["ip_is_private"] is not null);
        Assert.True(lan > 0);
        Assert.Equal("direct", (string?)rules[lan]["outbound"]);
        var app = rules.FindIndex(r => r["process_path_regex"] is { } rx && rx.ToJsonString().Contains("Test") && r["outbound"] is not null);
        Assert.True(app > lan);
        Assert.DoesNotContain(rules.Take(lan), r => r["outbound"] is not null);
    }

    [Fact]
    public void Switched_off_lan_has_no_direct_rule()
    {
        Assert.DoesNotContain(Rules(lan: false), r => r["ip_is_private"] is not null);
    }

    [Theory]
    [InlineData("http://192.168.3.2", true)]
    [InlineData("192.168.3.2:8080/admin", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("https://172.20.1.1", true)]
    [InlineData("169.254.1.1", true)]
    [InlineData("http://[fd00::1]/", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("https://ya.ru", false)]
    [InlineData("", false)]
    public void Recognises_local_network_addresses(string input, bool lan)
    {
        Assert.Equal(lan, DirectSites.IsLanAddress(input));
    }
}
