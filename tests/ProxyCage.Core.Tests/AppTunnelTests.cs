using System.Text.Json.Nodes;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class AppTunnelTests
{
    private static IReadOnlyList<ProxyNode> Nodes() =>
        SubscriptionParser.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "sub-example.txt")));

    private static string Folder(string name) =>
        Os.IsWindows ? $@"C:\Games\{name}" : $"/tmp/{name}";

    private static CehoConfig TwoApps() => new()
    {
        Apps =
        {
            new AppEntry { Name = "general", Folder = Folder("General") },
            new AppEntry { Name = "pinned", Folder = Folder("Pinned") },
        },
    };

    private static ProxyNode Dutch(IReadOnlyList<ProxyNode> nodes) =>
        nodes.First(n => n.CountryCode == "NL");

    private static ProxyNode Russian(IReadOnlyList<ProxyNode> nodes) =>
        nodes.First(n => n.CountryCode == "RU");

    private static JsonNode Root(IReadOnlyList<ProxyNode> nodes, CehoConfig cfg) =>
        JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(nodes, cfg))!;

    [Fact]
    public void Dns_answers_ipv4_only_so_doq_never_gets_aaaa()
    {
        var root = Root(Nodes(), TwoApps());
        Assert.Equal("ipv4_only", (string?)root["dns"]!["strategy"]);
    }

    [Fact]
    public void Dns_servers_do_not_include_google_public_dns()
    {
        var servers = Root(Nodes(), TwoApps())["dns"]!["servers"]!.AsArray();
        Assert.DoesNotContain(servers, s => (string?)s?["server"] is "8.8.8.8" or "8.8.4.4");
    }

    [Fact]
    public void Empty_filter_keeps_the_app_on_the_shared_pool()
    {
        var nodes = Nodes();
        var cfg = TwoApps();
        var root = Root(nodes, cfg);
        var rules = root["route"]!["rules"]!.AsArray();

        Assert.DoesNotContain(root["outbounds"]!.AsArray(), o =>
            ((string?)o!["tag"] ?? "").StartsWith("proxy-app-", StringComparison.Ordinal));

        Assert.Contains(rules, r =>
            (string?)r?["outbound"] == "proxy" && r["process_path_regex"] is JsonArray);
    }

    [Fact]
    public void Telegram_on_the_shared_pool_is_in_the_proxy_rule()
    {
        var cfg = new CehoConfig
        {
            Apps =
            {
                new AppEntry { Name = "probe-app", Folder = Folder("probe-app") },
                new AppEntry
                {
                    Name = "Telegram Desktop",
                    Folder = Os.IsWindows
                        ? @"C:\Users\CeBers_Dev\AppData\Roaming\Telegram Desktop"
                        : "/home/user/.local/share/TelegramDesktop",
                },
            },
        };

        var rules = Root(Nodes(), cfg)["route"]!["rules"]!.AsArray();
        var proxy = rules.First(r =>
            (string?)r?["outbound"] == "proxy" && r["process_path_regex"] is JsonArray);
        var regexes = proxy["process_path_regex"]!.AsArray().Select(x => (string)x!).ToList();

        Assert.Contains(regexes, rx => rx.Contains("Telegram", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Pinned_app_gets_its_own_urltest_and_leaves_the_other_app_on_proxy()
    {
        var nodes = Nodes();
        var cfg = TwoApps();
        var victim = Dutch(nodes);
        cfg.Apps[1].AllowedNodes.Add(victim.Key);

        var root = Root(nodes, cfg);
        var tag = SingBoxConfigGenerator.AppOutboundTag(1);
        var urltest = root["outbounds"]!.AsArray().First(o => (string?)o!["tag"] == tag);
        var pool = urltest!["outbounds"]!.AsArray().Select(x => (string)x!).ToList();

        Assert.Equal(new[] { victim.Tag }, pool);

        var rules = root["route"]!["rules"]!.AsArray();
        var pinnedRule = rules.First(r => (string?)r?["outbound"] == tag);
        Assert.Equal(AppDetector.ToRegex(cfg.Apps[1]), (string?)pinnedRule["process_path_regex"]![0]);

        var generalRule = rules.First(r =>
            (string?)r?["outbound"] == "proxy" && r["process_path_regex"] is JsonArray);
        var generalRegexes = generalRule["process_path_regex"]!.AsArray().Select(x => (string)x!).ToList();
        Assert.Contains(AppDetector.ToRegex(cfg.Apps[0]), generalRegexes);
        Assert.DoesNotContain(AppDetector.ToRegex(cfg.Apps[1]), generalRegexes);
    }

    [Fact]
    public void Pinned_node_of_a_forbidden_country_is_still_in_the_app_pool()
    {
        var nodes = Nodes();
        var cfg = TwoApps();
        var ru = Russian(nodes);
        cfg.Apps[1].AllowedNodes.Add(ru.Key);

        var root = Root(nodes, cfg);
        var appTags = root["outbounds"]!.AsArray()
            .First(o => (string?)o!["tag"] == SingBoxConfigGenerator.AppOutboundTag(1))!
            ["outbounds"]!.AsArray().Select(x => (string)x!).ToList();
        var shared = root["outbounds"]!.AsArray()
            .First(o => (string?)o!["tag"] == "proxy")!
            ["outbounds"]!.AsArray().Select(x => (string)x!).ToList();

        Assert.Contains(ru.Tag, appTags);
        Assert.DoesNotContain(ru.Tag, shared);
        Assert.Contains(root["outbounds"]!.AsArray(), o => (string?)o!["tag"] == ru.Tag);
    }

    [Fact]
    public void Missing_pinned_nodes_reject_the_app_instead_of_falling_back()
    {
        var nodes = Nodes();
        var cfg = TwoApps();
        cfg.Apps[1].AllowedNodes.Add("Vless|нет-такой.example.com|443");

        var root = Root(nodes, cfg);
        var rules = root["route"]!["rules"]!.AsArray();
        var reject = rules.First(r =>
            (string?)r?["action"] == "reject" && r["process_path_regex"] is JsonArray);

        Assert.Equal(AppDetector.ToRegex(cfg.Apps[1]), (string?)reject["process_path_regex"]![0]);
        Assert.DoesNotContain(root["outbounds"]!.AsArray(), o =>
            (string?)o!["tag"] == SingBoxConfigGenerator.AppOutboundTag(1));
    }

    [Fact]
    public void Own_processes_bypass_tunnel()
    {
        var root = Root(Nodes(), TwoApps());
        var own = SingBoxConfigGenerator.OwnProcessRegexes().Select(x => (string)x!).ToList();

        var direct = root["route"]!["rules"]!.AsArray().FirstOrDefault(r =>
            (string?)r?["outbound"] == "direct"
            && r["process_path_regex"] is JsonArray arr
            && own.All(rx => arr.Any(x => string.Equals((string?)x, rx, StringComparison.Ordinal))));

        Assert.NotNull(direct);

        var dnsOwn = root["dns"]!["rules"]!.AsArray().FirstOrDefault(r =>
            (string?)r?["server"] == "dns-direct"
            && r["process_path_regex"] is JsonArray arr
            && own.All(rx => arr.Any(x => string.Equals((string?)x, rx, StringComparison.Ordinal))));

        Assert.NotNull(dnsOwn);
        Assert.Matches(own[0], @"C:\ProgramData\CehoProxy\cehoproxy.exe");
    }

    [Fact]
    public void Allowed_nodes_survive_saving_and_loading()
    {
        var path = Path.Combine(Path.GetTempPath(), "chp-tunnel-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        try
        {
            var cfg = TwoApps();
            cfg.Apps[1].AllowedNodes.Add(Dutch(Nodes()).Key);
            cfg.Save(path);

            var loaded = CehoConfig.Load(path);
            Assert.Equal(cfg.Apps[1].AllowedNodes, loaded.Apps[1].AllowedNodes);
            Assert.Empty(loaded.Apps[0].AllowedNodes);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Comma_list_picks_several_indexes_and_empty_means_none()
    {
        Assert.True(IndexList.TryParse("1,3, 3,2", 4, out var indexes));
        Assert.Equal(new[] { 0, 2, 1 }, indexes);

        Assert.True(IndexList.TryParse("  ", 4, out var none));
        Assert.Empty(none);

        Assert.False(IndexList.TryParse("1,9", 4, out _));
        Assert.False(IndexList.TryParse("x", 4, out _));
    }

    [Fact]
    public void Quic_udp_443_is_rejected_for_isolated_apps_and_mixed_in()
    {
        var root = Root(Nodes(), TwoApps());
        var rules = root["route"]!["rules"]!.AsArray();

        // Mixed-in rejects UDP 443
        Assert.Contains(rules, r =>
            (string?)r?["action"] == "reject"
            && (string?)r?["network"] == "udp"
            && r["port"] is JsonArray ports && ports.Any(p => (int)p! == 443)
            && r["inbound"] is JsonArray inb && inb.Any(i => (string?)i == "mixed-in"));

        Assert.Contains(rules, r =>
            (string?)r?["action"] == "reject"
            && (string?)r?["network"] == "udp"
            && r["port"] is JsonArray ports && ports.Any(p => (int)p! == 853)
            && r["inbound"] is JsonArray inb && inb.Any(i => (string?)i == "mixed-in"));

        // Isolated apps reject UDP 443 before proxy
        Assert.Contains(rules, r =>
            (string?)r?["action"] == "reject"
            && (string?)r?["network"] == "udp"
            && r["port"] is JsonArray ports && ports.Any(p => (int)p! == 443)
            && r["process_path_regex"] is JsonArray);
    }
}
