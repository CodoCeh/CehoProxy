using System.Text.Json;
using System.Text.Json.Nodes;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class SingBoxConfigGeneratorTests
{
    private static IReadOnlyList<ProxyNode> Nodes()
        => SubscriptionParser.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "sub-example.txt")));

    private static ProxyCageSettings Settings() => new()
    {
        FolderPath = @"C:\Games\MyApp",
        ExcludedExitCountries = new(StringComparer.OrdinalIgnoreCase) { "RU", "NL" },
        BlockedDestinationCountries = new() { "RU", "NL" },
        RuleSetDir = "rulesets",
    };

    [Fact]
    public void Excludes_nl_and_ru_from_pool()
    {
        var json = SingBoxConfigGenerator.Generate(Nodes(), Settings());
        var root = JsonNode.Parse(json)!;
        var proxy = root["outbounds"]!.AsArray()
            .First(o => (string?)o!["tag"] == "proxy")!;
        var poolTags = proxy["outbounds"]!.AsArray().Select(x => (string)x!).ToList();

        // из 13 реальных нод убираем 5 NL + 1 RU = 7 остаётся
        // (в их числе нода без распознанной страны — она не исключена)
        Assert.Equal(7, poolTags.Count);

        // ни одна из нод пула не должна быть NL или RU
        var nodesByTag = Nodes().ToDictionary(n => n.Tag);
        foreach (var tag in poolTags)
        {
            var cc = nodesByTag[tag].CountryCode;
            Assert.False(cc is "NL" or "RU", $"нода {tag} страны {cc} не должна быть в пуле");
        }
    }

    [Fact]
    public void Folder_traffic_routed_to_proxy_not_direct()
    {
        var json = SingBoxConfigGenerator.Generate(Nodes(), Settings());
        var root = JsonNode.Parse(json)!;
        var rules = root["route"]!["rules"]!.AsArray();

        // финальный маршрут — direct (остальная система не затрагивается)
        Assert.Equal("direct", (string?)root["route"]!["final"]);

        // есть правило: процессы папки → outbound proxy
        Assert.Contains(rules, r =>
            (string?)r?["outbound"] == "proxy" &&
            r["process_path_regex"] is JsonArray);
    }

    [Fact]
    public void Blocks_ru_nl_destinations_via_reject()
    {
        var json = SingBoxConfigGenerator.Generate(Nodes(), Settings());
        var root = JsonNode.Parse(json)!;
        var rules = root["route"]!["rules"]!.AsArray();

        var rejectRule = rules.FirstOrDefault(r => (string?)r?["action"] == "reject");
        Assert.NotNull(rejectRule);
        var rsTags = rejectRule!["rule_set"]!.AsArray().Select(x => (string)x!).ToList();
        Assert.Contains("geoip-ru", rsTags);
        Assert.Contains("geoip-nl", rsTags);
    }

    [Fact]
    public void Throws_when_all_countries_excluded()
    {
        var s = Settings();
        s.ExcludedExitCountries = new(StringComparer.OrdinalIgnoreCase)
            { "RU", "NL", "DE", "PL", "FI", "US", "TR", CountryResolver.Unknown };
        Assert.Throws<InvalidOperationException>(() => SingBoxConfigGenerator.Generate(Nodes(), s));
    }

    [Fact]
    public void Keeps_unknown_country_nodes_unless_they_are_excluded_too()
    {
        var s = Settings();
        s.ExcludedExitCountries = new(StringComparer.OrdinalIgnoreCase)
            { "RU", "NL", "DE", "PL", "FI", "US", "TR" };

        // все известные страны сняты, но нода с нераспознанной страной остаётся:
        // выбрасывать её не за что, и туннель поднимется
        var json = SingBoxConfigGenerator.Generate(Nodes(), s);
        var proxy = JsonNode.Parse(json)!["outbounds"]!.AsArray()
            .First(o => (string?)o!["tag"] == "proxy")!;
        Assert.NotEmpty(proxy["outbounds"]!.AsArray());
    }

    [Theory]
    [InlineData(@"C:\Games\App", @"(?i)^C:\\Games\\App[\\/]")]
    [InlineData(@"C:\Games\App\", @"(?i)^C:\\Games\\App[\\/]")]
    public void FolderPathToRegex_escapes_and_anchors(string input, string expected)
    {
        Assert.Equal(expected, SingBoxConfigGenerator.FolderPathToRegex(input));
    }

    [Fact]
    public void Grpc_node_has_no_flow_but_has_service_name()
    {
        var json = SingBoxConfigGenerator.Generate(Nodes(), Settings());
        var root = JsonNode.Parse(json)!;
        var grpc = root["outbounds"]!.AsArray()
            .FirstOrDefault(o => o?["transport"]?["type"]?.GetValue<string>() == "grpc");
        Assert.NotNull(grpc);
        Assert.Null(grpc!["flow"]); // flow недопустим для grpc
        Assert.NotNull(grpc["transport"]!["service_name"]);
    }

    /// <summary>
    /// Ноды vless+reality из настоящей подписки владельца: движок отказывается
    /// от рукопожатия, если потерять short_id, serviceName или flow. Разбирали живьём,
    /// когда владелец сообщил «подписка рабочая, а нод нет» — тогда причина была в другом,
    /// но проверка полей осталась, чтобы генератор не начал их терять молча.
    /// </summary>
    [Fact]
    public void Keeps_reality_short_id_grpc_and_flow()
    {
        const string sub =
            "vless://11111111-2222-3333-4444-555555555555@grpc.example:8444?encryption=none&type=grpc" +
            "&serviceName=GunService&mode=gun&security=reality&sni=github.com&fp=chrome" +
            "&pbk=FWitre1jinQR7qz5HQ0q2Q4Gg9xA9B0SyizCziN7Ch0&sid=457fab2ac885dbe0#\U0001F1E9\U0001F1EA Германия\n" +
            "vless://11111111-2222-3333-4444-555555555555@vision.example:443?encryption=none" +
            "&flow=xtls-rprx-vision&type=tcp&security=reality&sni=github.com&fp=chrome" +
            "&pbk=FWitre1jinQR7qz5HQ0q2Q4Gg9xA9B0SyizCziN7Ch0&sid=457fab2ac885dbe0#\U0001F1FA\U0001F1F8 США";

        var json = SingBoxConfigGenerator.Generate(SubscriptionParser.Parse(sub), new ProxyCageSettings
        {
            FolderPath = @"C:\Games\MyApp",
            RuleSetDir = "rulesets",
        });
        var outbounds = JsonNode.Parse(json)!["outbounds"]!.AsArray();

        var grpc = outbounds.First(o => (string?)o!["server"] == "grpc.example")!;
        Assert.Equal("457fab2ac885dbe0", (string?)grpc["tls"]!["reality"]!["short_id"]);
        Assert.Equal("GunService", (string?)grpc["transport"]!["service_name"]);
        Assert.Equal("chrome", (string?)grpc["tls"]!["utls"]!["fingerprint"]);

        var vision = outbounds.First(o => (string?)o!["server"] == "vision.example")!;
        Assert.Equal("xtls-rprx-vision", (string?)vision["flow"]);
        Assert.Equal("457fab2ac885dbe0", (string?)vision["tls"]!["reality"]!["short_id"]);
    }
}
