using System.Text.Json.Nodes;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class SiteListScopeTests
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

    private static bool Direct(JsonNode? rule) => (string?)rule?["outbound"] == "direct";

    private static CehoConfig Config(string mode) => new()
    {
        SiteMode = mode,
        Apps =
        {
            new AppEntry { Name = "Telegram Desktop", Folder = "/tmp/Telegram Desktop" },
            new AppEntry { Name = "Codex", Folder = "/tmp/codex-app" },
            new AppEntry { Name = "Chrome", Folder = "/tmp/chrome-app" },
        },
        DirectSites = { "ya.ru" },
    };

    [Theory]
    [InlineData(CehoConfig.SiteModeOnly)]
    [InlineData(CehoConfig.SiteModeExcept)]
    public void Apps_other_than_browsers_ignore_the_site_list(string mode)
    {
        var rules = Rules(Config(mode));
        foreach (var folder in new[] { "/tmp/Telegram Desktop", "/tmp/codex-app" })
        {
            Assert.Contains(rules, r => Mentions(r, folder) && (string?)r!["outbound"] == "proxy" && r["domain_suffix"] is null);
            Assert.DoesNotContain(rules, r => Mentions(r, folder) && (Direct(r) || r!["domain_suffix"] is not null));
        }
        Assert.DoesNotContain(rules, r => Direct(r) && r!["domain_suffix"] is not null
            && r["process_path_regex"] is null && r["inbound"] is null);
    }

    [Fact]
    public void Browser_in_only_mode_sends_the_list_through_the_tunnel_and_the_rest_direct()
    {
        var rules = Rules(Config(CehoConfig.SiteModeOnly));
        Assert.Contains(rules, r => Mentions(r, "/tmp/chrome-app") && (string?)r!["outbound"] == "proxy" && r["domain_suffix"] is JsonArray);
        Assert.Contains(rules, r => Mentions(r, "/tmp/chrome-app") && Direct(r) && r!["domain_suffix"] is null);
    }

    [Fact]
    public void Browser_and_proxy_in_except_mode_send_the_list_direct()
    {
        var rules = Rules(Config(CehoConfig.SiteModeExcept));
        Assert.Contains(rules, r => Mentions(r, "/tmp/chrome-app") && Direct(r) && r!["domain_suffix"] is JsonArray);
        Assert.Contains(rules, r => r?["inbound"] is JsonArray i && i.Any(x => (string?)x == "mixed-in")
            && Direct(r) && r["domain_suffix"] is JsonArray);
        Assert.DoesNotContain(rules, r => Mentions(r, "/tmp/Telegram Desktop") && r!["domain_suffix"] is JsonArray);
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
        Assert.DoesNotContain(rules, r => Mentions(r, "Telegram Desktop") && Direct(r));
    }
}
