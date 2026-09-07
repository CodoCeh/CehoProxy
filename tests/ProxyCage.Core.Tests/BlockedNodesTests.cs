using System.Text.Json.Nodes;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class BlockedNodesTests
{
    private static IReadOnlyList<ProxyNode> Nodes() =>
        SubscriptionParser.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "sub-example.txt")));

    private static CehoConfig Config() => new()
    {
        Apps = { new AppEntry { Name = "app", Folder = Os.IsWindows ? @"C:\Games\App" : "/tmp" } },
    };

    private static ProxyNode Dutch(IReadOnlyList<ProxyNode> nodes) =>
        nodes.First(n => n.CountryCode == "NL");

    [Fact]
    public void Blocked_node_leaves_the_pool_and_its_country_stays()
    {
        var nodes = Nodes();
        var cfg = Config();
        var victim = Dutch(nodes);
        var dutchBefore = SingBoxConfigGenerator.BuildPool(nodes, cfg).Count(n => n.CountryCode == "NL");

        cfg.BlockedNodes.Add(victim.Key);
        var pool = SingBoxConfigGenerator.BuildPool(nodes, cfg);

        Assert.DoesNotContain(pool, n => n.Key == victim.Key);
        Assert.Equal(dutchBefore - 1, pool.Count(n => n.CountryCode == "NL"));
        Assert.True(dutchBefore > 1, "в наборе должно быть несколько нод Нидерландов");
    }

    [Fact]
    public void Blocked_node_is_absent_from_the_engine_config()
    {
        var nodes = Nodes();
        var cfg = Config();
        var victim = Dutch(nodes);
        cfg.BlockedNodes.Add(victim.Key);

        var root = JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(nodes, cfg))!;
        var tags = root["outbounds"]!.AsArray()
            .First(o => (string?)o!["tag"] == "proxy")!["outbounds"]!
            .AsArray().Select(x => (string)x!).ToList();

        Assert.DoesNotContain(victim.Tag, tags);
        Assert.NotEmpty(tags);
    }

    [Fact]
    public void Block_is_remembered_by_address_so_a_new_tag_does_not_revive_it()
    {
        var cfg = Config();
        var victim = Dutch(Nodes());
        cfg.BlockedNodes.Add(victim.Key);

        // Обновление подписки перенумеровывает ноды: тег меняется, адрес остаётся.
        var afterRefresh = Nodes();
        foreach (var n in afterRefresh) n.Tag = "renamed-" + n.Tag;

        var pool = SingBoxConfigGenerator.BuildPool(afterRefresh, cfg);

        Assert.DoesNotContain(pool, n => n.Key == victim.Key);
    }

    [Fact]
    public void Turning_off_everything_by_hand_is_refused_with_its_own_reason()
    {
        var nodes = Nodes();
        var cfg = Config();
        foreach (var n in nodes.Where(n => !n.IsMeta)) cfg.BlockedNodes.Add(n.Key);

        var ex = Assert.Throws<PoolEmptyException>(() => SingBoxConfigGenerator.BuildPool(nodes, cfg));

        Assert.Equal(Strings.T("ru", "nodes_none_left"), ex.Message);
    }

    [Fact]
    public void Excluded_country_is_still_reported_as_the_country_problem()
    {
        var nodes = Nodes();
        var cfg = Config();
        cfg.PreferredCountries.Add("JP");

        var ex = Assert.Throws<PoolEmptyException>(() => SingBoxConfigGenerator.BuildPool(nodes, cfg));

        Assert.Contains("JP", ex.Message);
    }

    [Fact]
    public void Blocking_a_node_of_a_forbidden_country_changes_nothing()
    {
        var nodes = Nodes();
        var cfg = Config();
        var russian = nodes.First(n => n.CountryCode == "RU");

        var before = SingBoxConfigGenerator.BuildPool(nodes, cfg).Count;
        cfg.BlockedNodes.Add(russian.Key);

        Assert.Equal(before, SingBoxConfigGenerator.BuildPool(nodes, cfg).Count);
    }

    [Fact]
    public void Unknown_keys_in_the_list_are_harmless()
    {
        var nodes = Nodes();
        var cfg = Config();
        cfg.BlockedNodes.Add("Vless|нет-такой-ноды.example.com|443");

        Assert.Equal(
            SingBoxConfigGenerator.BuildPool(nodes, new CehoConfig
            {
                Apps = { new AppEntry { Name = "app", Folder = "/tmp" } },
            }).Count,
            SingBoxConfigGenerator.BuildPool(nodes, cfg).Count);
    }

    [Fact]
    public void Case_of_the_saved_key_does_not_matter()
    {
        var nodes = Nodes();
        var cfg = Config();
        var victim = Dutch(nodes);
        cfg.BlockedNodes.Add(victim.Key.ToUpperInvariant());

        Assert.DoesNotContain(SingBoxConfigGenerator.BuildPool(nodes, cfg), n => n.Key == victim.Key);
    }

    [Fact]
    public void Blocked_list_survives_saving_and_loading()
    {
        var path = Path.Combine(Path.GetTempPath(), "chp-cfg-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        try
        {
            var cfg = Config();
            cfg.BlockedNodes.Add(Dutch(Nodes()).Key);
            cfg.Save(path);

            Assert.Equal(cfg.BlockedNodes, CehoConfig.Load(path).BlockedNodes);
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
