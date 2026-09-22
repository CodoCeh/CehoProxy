using System.Text.Json.Nodes;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class DirectSiteTests
{
    [Theory]
    [InlineData("https://www.sber.ru/person", "sber.ru")]
    [InlineData("ya.ru/search?q=1", "ya.ru")]
    [InlineData("https://YA.RU:443", "ya.ru")]
    [InlineData("https://яндекс.рф/maps", "яндекс.рф")]
    public void Link_becomes_a_site_without_path(string raw, string host) =>
        Assert.Equal(host, DirectSites.Normalize(raw));

    [Fact]
    public void Preset_adds_only_sites_that_are_not_already_listed()
    {
        foreach (var host in DirectSites.TunnelSites.Concat(DirectSites.RussianSites))
            Assert.Equal(host, DirectSites.Normalize(host));
        Assert.Empty(DirectSites.TunnelSites.Intersect(DirectSites.RussianSites, StringComparer.OrdinalIgnoreCase));

        var fresh = DirectSites.NewHosts(["YouTube.com"], DirectSites.Preset(throughTunnel: true));
        Assert.DoesNotContain("youtube.com", fresh);
        Assert.Contains("google.com", fresh);
        Assert.Equal(DirectSites.TunnelSites.Length - 1, fresh.Count);
        Assert.Contains("yandex.ru", DirectSites.Preset(throughTunnel: false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a site")]
    [InlineData("https://127.0.0.1/")]
    public void Junk_is_rejected(string raw) =>
        Assert.Null(DirectSites.Normalize(raw));

    [Fact]
    public void Listed_site_goes_direct_before_the_app_tunnel()
    {
        var nodes = SubscriptionParser.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "sub-example.txt")));
        var cfg = new CehoConfig
        {
            Apps = { new AppEntry { Name = "app", Folder = Os.IsWindows ? @"C:\Games\App" : "/tmp/app" } },
            DirectSites = { "https://www.sber.ru/person" },
        };

        var root = JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(nodes, cfg))!;
        var rules = root["route"]!["rules"]!.AsArray();
        var directAt = IndexOf(rules, r =>
            (string?)r?["outbound"] == "direct" && r["domain_suffix"] is JsonArray);
        var tunnelAt = IndexOf(rules, r =>
            (string?)r?["outbound"] == "proxy" && r["process_path_regex"] is JsonArray);

        Assert.True(directAt >= 0 && directAt < tunnelAt);
        Assert.Equal("sber.ru", (string?)rules[directAt]!["domain_suffix"]![0]);

        var dns = root["dns"]!["rules"]!.AsArray();
        var dnsAt = IndexOf(dns, r => r?["domain_suffix"] is JsonArray);
        var processDns = IndexOf(dns, r => r?["process_path_regex"] is JsonArray);
        Assert.True(dnsAt >= 0 && dnsAt < processDns);
        Assert.Equal("dns-direct", (string?)dns[dnsAt]!["server"]);
    }

    [Fact]
    public void Empty_list_adds_no_site_rule()
    {
        var nodes = SubscriptionParser.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "sub-example.txt")));
        var cfg = new CehoConfig
        {
            Apps = { new AppEntry { Name = "app", Folder = Os.IsWindows ? @"C:\Games\App" : "/tmp/app" } },
        };
        var rules = JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(nodes, cfg))!["route"]!["rules"]!.AsArray();
        Assert.DoesNotContain(rules, r => r?["domain_suffix"] is JsonArray);
    }

    [Fact]
    public void Only_mode_sends_the_list_through_the_tunnel_and_the_rest_direct()
    {
        var nodes = SubscriptionParser.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "sub-example.txt")));
        var cfg = new CehoConfig
        {
            SiteMode = CehoConfig.SiteModeOnly,
            Apps = { new AppEntry { Name = "app", Folder = "/tmp/ceho-site-app" } },
            DirectSites = { "ya.ru" },
        };

        var root = JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(nodes, cfg))!;
        var rules = root["route"]!["rules"]!.AsArray();
        var listed = IndexOf(rules, r =>
            (string?)r?["outbound"] == "proxy"
            && r["domain_suffix"] is JsonArray
            && r["process_path_regex"] is JsonArray);
        var rest = IndexOf(rules, r =>
            (string?)r?["outbound"] == "direct"
            && r["domain_suffix"] is null
            && r["process_path_regex"] is JsonArray
            && r["process_path_regex"]!.AsArray().Any(x => ((string?)x)?.Contains("/tmp/ceho-site-app", StringComparison.Ordinal) == true));
        var browserListed = IndexOf(rules, r =>
            (string?)r?["outbound"] == "proxy"
            && r["domain_suffix"] is JsonArray
            && InboundIs(r, "mixed-in"));
        var browserRest = IndexOf(rules, r =>
            (string?)r?["outbound"] == "direct"
            && r["domain_suffix"] is null
            && InboundIs(r, "mixed-in"));

        Assert.True(listed >= 0 && listed < rest);
        Assert.True(browserListed >= 0 && browserListed < browserRest);
        Assert.Equal("ya.ru", (string?)rules[listed]!["domain_suffix"]![0]);
        Assert.DoesNotContain(rules, r =>
            (string?)r?["outbound"] == "direct" && r["domain_suffix"] is JsonArray && r["process_path_regex"] is null);

        var dns = root["dns"]!["rules"]!.AsArray();
        var listedDns = IndexOf(dns, r =>
            (string?)r?["server"] == "dns-proxy" && r["domain_suffix"] is JsonArray && r["process_path_regex"] is JsonArray);
        var restDns = IndexOf(dns, r =>
            (string?)r?["server"] == "dns-direct"
            && r["domain_suffix"] is null
            && r["process_path_regex"] is JsonArray
            && r["process_path_regex"]!.AsArray().Any(x => ((string?)x)?.Contains("/tmp/ceho-site-app", StringComparison.Ordinal) == true));
        Assert.True(listedDns >= 0 && listedDns < restDns);
    }

    [Fact]
    public void Only_mode_keeps_a_pinned_app_on_its_own_exit()
    {
        var nodes = SubscriptionParser.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "sub-example.txt")));
        var pinned = nodes.First(n => n.CountryCode == "NL");
        var cfg = new CehoConfig
        {
            SiteMode = CehoConfig.SiteModeOnly,
            Apps = { new AppEntry { Name = "app", Folder = "/tmp/pinned-app", AllowedNodes = { pinned.Key } } },
            DirectSites = { "ya.ru" },
        };

        var rules = JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(nodes, cfg))!["route"]!["rules"]!.AsArray();
        var tag = SingBoxConfigGenerator.AppOutboundTag(0);
        Assert.Contains(rules, r =>
            (string?)r?["outbound"] == tag && r["domain_suffix"] is JsonArray);
        Assert.DoesNotContain(rules, r =>
            (string?)r?["outbound"] == "proxy" && r["process_path_regex"] is JsonArray);
    }

    [Fact]
    public void Empty_only_list_sends_apps_and_the_browser_direct()
    {
        var nodes = SubscriptionParser.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "sub-example.txt")));
        var cfg = new CehoConfig
        {
            SiteMode = CehoConfig.SiteModeOnly,
            Apps = { new AppEntry { Name = "app", Folder = "/tmp/app" } },
        };
        var rules = JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(nodes, cfg))!["route"]!["rules"]!.AsArray();
        Assert.DoesNotContain(rules, r => r?["domain_suffix"] is JsonArray);
        Assert.Contains(rules, r =>
            (string?)r?["outbound"] == "direct" && r["process_path_regex"] is JsonArray);
        Assert.Contains(rules, r =>
            (string?)r?["outbound"] == "direct" && InboundIs(r, "mixed-in") && r["domain_suffix"] is null);
    }

    [Fact]
    public void Chosen_country_sends_that_site_through_its_nodes_and_leaves_the_other_direct()
    {
        var nodes = Nodes();
        var cfg = App();
        cfg.DirectSites.Add("ya.ru");
        cfg.DirectSites.Add("example.com");
        cfg.SiteCountries["ya.ru"] = "nl";

        var root = JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(nodes, cfg))!;
        var tag = SingBoxConfigGenerator.SiteOutboundTag("NL");
        var picked = root["outbounds"]!.AsArray().First(o => (string?)o!["tag"] == tag)!["outbounds"]!
            .AsArray().Select(x => (string)x!).ToList();
        var dutch = nodes.Where(n => n.CountryCode == "NL" && !n.IsMeta).Select(n => n.Tag).ToList();
        Assert.Equal(dutch, picked);

        var rules = root["route"]!["rules"]!.AsArray();
        var countryAt = IndexOf(rules, r => (string?)r?["outbound"] == tag && r["domain_suffix"] is JsonArray);
        var direct = rules.First(r =>
            (string?)r?["outbound"] == "direct" && r["domain_suffix"] is JsonArray && r["process_path_regex"] is null);
        var directHosts = direct["domain_suffix"]!.AsArray().Select(x => (string)x!).ToList();
        Assert.True(countryAt >= 0);
        Assert.Equal("ya.ru", (string?)rules[countryAt]!["domain_suffix"]![0]);
        Assert.Contains("example.com", directHosts);
        Assert.DoesNotContain("ya.ru", directHosts);
    }

    [Fact]
    public void Chosen_country_can_be_one_the_shared_pool_does_not_use()
    {
        var nodes = Nodes();
        var cfg = App();
        cfg.DirectSites.Add("ya.ru");
        cfg.SiteCountries["ya.ru"] = "RU";

        var root = JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(nodes, cfg))!;
        var russian = nodes.Where(n => n.CountryCode == "RU" && !n.IsMeta).Select(n => n.Tag).ToList();
        var sitePool = root["outbounds"]!.AsArray()
            .First(o => (string?)o!["tag"] == SingBoxConfigGenerator.SiteOutboundTag("RU"))!["outbounds"]!
            .AsArray().Select(x => (string)x!).ToList();
        var shared = root["outbounds"]!.AsArray().First(o => (string?)o!["tag"] == "proxy")!["outbounds"]!
            .AsArray().Select(x => (string)x!).ToList();

        Assert.Equal(russian, sitePool);
        Assert.DoesNotContain(russian, tag => shared.Contains(tag));
    }

    [Fact]
    public void Site_country_overrides_the_app_pin_only_for_that_site()
    {
        var nodes = Nodes();
        var cfg = App();
        cfg.Apps[0].AllowedNodes.Add(nodes.First(n => n.CountryCode == "NL").Key);
        cfg.DirectSites.Add("ya.ru");
        cfg.SiteCountries["ya.ru"] = "RU";

        var rules = JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(nodes, cfg))!["route"]!["rules"]!.AsArray();
        Assert.Contains(rules, r =>
            (string?)r?["outbound"] == SingBoxConfigGenerator.SiteOutboundTag("RU")
            && r["domain_suffix"] is JsonArray
            && r["process_path_regex"] is JsonArray);
        Assert.Contains(rules, r =>
            (string?)r?["outbound"] == SingBoxConfigGenerator.AppOutboundTag(0)
            && r["domain_suffix"] is null
            && r["process_path_regex"] is JsonArray);
    }

    private static IReadOnlyList<ProxyNode> Nodes() =>
        SubscriptionParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "sub-example.txt")));

    private static CehoConfig App() => new()
    {
        Apps = { new AppEntry { Name = "app", Folder = "/tmp/ceho-site-app" } },
    };

    private static bool InboundIs(JsonNode? rule, string tag) =>
        rule?["inbound"] is JsonArray inbound && inbound.Any(x => (string?)x == tag);

    private static int IndexOf(JsonArray rules, Func<JsonNode?, bool> match)
    {
        for (var i = 0; i < rules.Count; i++)
            if (match(rules[i])) return i;
        return -1;
    }
}
