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

        Assert.Equal(7, poolTags.Count);

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

        Assert.Equal("direct", (string?)root["route"]!["final"]);

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
        Assert.Null(grpc!["flow"]);
        Assert.NotNull(grpc["transport"]!["service_name"]);
        Assert.Equal("60s", (string?)grpc["transport"]!["idle_timeout"]);
        Assert.Equal("20s", (string?)grpc["transport"]!["ping_timeout"]);
        Assert.True(grpc["transport"]!["permit_without_stream"]!.GetValue<bool>());
    }

    [Fact]
    public void Ws_transport_has_only_fields_sing_box_accepts()
    {
        var node = SubscriptionParser.Parse(
            "vless://11111111-1111-1111-1111-111111111111@198.51.100.10:443?type=ws&path=%2Fws&host=cdn.example&security=tls&sni=cdn.example#WS").Single();
        var transport = OutboundBuilder.Build(node)["transport"]!.AsObject();

        Assert.Equal("ws", (string?)transport["type"]);
        Assert.Empty(transport.Select(p => p.Key).Except(new[] { "type", "path", "headers", "max_early_data", "early_data_header_name" }));
    }

    [Fact]
    public void Generated_config_is_decoded_by_sing_box_when_engine_is_given()
    {
        var engine = Environment.GetEnvironmentVariable("CEHO_SINGBOX_CHECK");
        if (string.IsNullOrEmpty(engine) || !File.Exists(engine)) return;

        var nodes = SubscriptionParser.Parse(string.Join("\n",
            "vless://11111111-1111-1111-1111-111111111111@198.51.100.10:443?type=ws&path=%2Fws&host=cdn.example&security=tls&sni=cdn.example#WS",
            "vless://11111111-1111-1111-1111-111111111111@198.51.100.11:443?type=grpc&serviceName=g&security=tls&sni=g.example#GRPC",
            "vmess://" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
                "{\"v\":\"2\",\"ps\":\"VMWS\",\"add\":\"198.51.100.12\",\"port\":\"443\",\"id\":\"11111111-1111-1111-1111-111111111111\",\"net\":\"ws\",\"path\":\"/v\",\"tls\":\"tls\"}")),
            "trojan://secret@198.51.100.13:443?type=ws&path=%2Ft&sni=t.example#TRWS"));
        var config = Path.Combine(Path.GetTempPath(), $"ceho-check-{Guid.NewGuid():N}.json");
        File.WriteAllText(config, SingBoxConfigGenerator.Generate(nodes, new ProxyCageSettings { FolderPath = @"C:\Games\MyApp" }));
        try
        {
            var run = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(engine, $"check -c \"{config}\"")
                { RedirectStandardError = true, RedirectStandardOutput = true })!;
            var error = run.StandardError.ReadToEnd();
            run.WaitForExit();
            Assert.DoesNotContain("decode config", error);
        }
        finally { File.Delete(config); }
    }

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

    [Fact]
    public void Ceho_runtime_config_exposes_clash_api_for_urltest_delays()
    {
        var cfg = new CehoConfig
        {
            Apps = { new AppEntry { Name = "Test", Folder = @"C:\Apps\Test", Enabled = true } },
            ClashApiPort = 19090,
        };
        var json = SingBoxConfigGenerator.GenerateForConfig(Nodes(), cfg);
        var controller = JsonNode.Parse(json)!["experimental"]!["clash_api"]!["external_controller"];

        Assert.Equal("127.0.0.1:19090", (string?)controller);
    }
}
