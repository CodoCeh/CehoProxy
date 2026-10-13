using System.Text.Json.Nodes;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class YoutubeAdsTests
{
    private static IReadOnlyList<ProxyNode> Nodes()
        => SubscriptionParser.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "sub-example.txt")));

    private static JsonArray Rules(CehoConfig cfg)
        => JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(Nodes(), cfg))!["route"]!["rules"]!.AsArray();

    private static CehoConfig Config(bool block) => new()
    {
        Apps = { new AppEntry { Name = "Test", Folder = @"C:\Apps\Test", Enabled = true } },
        BlockYoutubeAds = block,
    };

    [Fact]
    public void Enabled_adds_reject_rule_for_ad_domains_right_after_sniff()
    {
        var rules = Rules(Config(block: true));
        var reject = rules[1]!.AsObject();
        Assert.Equal("reject", (string?)reject["action"]);
        var suffixes = reject["domain_suffix"]!.AsArray().Select(x => (string)x!).ToList();
        Assert.Contains("doubleclick.net", suffixes);
        Assert.Contains("googlesyndication.com", suffixes);
    }

    [Fact]
    public void Disabled_adds_no_ad_reject_rule()
    {
        var rules = Rules(Config(block: false));
        Assert.DoesNotContain(rules, r => r!["domain_suffix"] is JsonArray s && s.Any(x => (string?)x == "doubleclick.net"));
    }

    [Fact]
    public void Setting_is_independent_of_subscription_nodes()
    {
        var cfg = Config(block: true);
        cfg.Subscriptions.Clear();
        var rules = Rules(cfg);
        Assert.Contains(rules, r => r!["domain_suffix"] is JsonArray s && s.Any(x => (string?)x == "doubleclick.net"));
    }
}
