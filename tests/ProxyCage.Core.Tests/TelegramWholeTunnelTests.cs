using System.Text.Json.Nodes;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class TelegramWholeTunnelTests
{
    private static JsonArray Rules(CehoConfig cfg)
    {
        var nodes = SubscriptionParser.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "sub-example.txt")));
        return JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(nodes, cfg))!["route"]!["rules"]!.AsArray();
    }

    private static bool Mentions(JsonNode? rule, string folder) =>
        rule?["process_path_regex"] is JsonArray rx
        && rx.Any(x => ((string?)x)?.Contains(folder, StringComparison.Ordinal) == true);

    [Fact]
    public void Telegram_goes_whole_through_vpn_in_only_mode()
    {
        var cfg = new CehoConfig
        {
            SiteMode = CehoConfig.SiteModeOnly,
            Apps =
            {
                new AppEntry { Name = "Telegram Desktop", Folder = "/tmp/Telegram Desktop" },
                new AppEntry { Name = "app", Folder = "/tmp/ceho-site-app" },
            },
            DirectSites = { "ya.ru" },
        };

        var rules = Rules(cfg);
        Assert.Contains(rules, r => Mentions(r, "Telegram Desktop")
            && (string?)r!["outbound"] == "proxy" && r["domain_suffix"] is null);
        Assert.DoesNotContain(rules, r => Mentions(r, "Telegram Desktop") && (string?)r!["outbound"] == "direct");
        Assert.Contains(rules, r => Mentions(r, "/tmp/ceho-site-app")
            && (string?)r!["outbound"] == "direct" && r["domain_suffix"] is null);
    }

    [Fact]
    public void Pinned_telegram_goes_whole_through_its_node_in_only_mode()
    {
        var nodes = SubscriptionParser.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "sub-example.txt")));
        var cfg = new CehoConfig
        {
            SiteMode = CehoConfig.SiteModeOnly,
            Apps = { new AppEntry { Name = "Telegram", Folder = "/tmp/Telegram Desktop", AllowedNodes = { nodes.First(n => n.CountryCode == "NL").Key } } },
            DirectSites = { "ya.ru" },
        };

        var rules = Rules(cfg);
        var tag = SingBoxConfigGenerator.AppOutboundTag(0);
        Assert.Contains(rules, r => Mentions(r, "Telegram Desktop")
            && (string?)r!["outbound"] == tag && r["domain_suffix"] is null);
        Assert.DoesNotContain(rules, r => Mentions(r, "Telegram Desktop") && (string?)r!["outbound"] == "direct");
    }
}
