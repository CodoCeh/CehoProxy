using System.Text.Json.Nodes;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class EngineLogSettingTests
{
    private static JsonNode Runtime(CehoConfig cfg)
    {
        var nodes = SubscriptionParser.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "sub-example.txt")));
        return JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(nodes, cfg))!;
    }

    private static CehoConfig WithApp(string? level = null)
    {
        var cfg = new CehoConfig
        {
            Apps = { new AppEntry { Name = "app", Folder = Os.IsWindows ? @"C:\Games\App" : "/tmp" } },
        };
        if (level is not null) cfg.EngineLogLevel = level;
        return cfg;
    }

    [Fact]
    public void Engine_writes_to_us_not_to_a_file_of_its_own()
    {
        var log = Runtime(WithApp())!["log"]!;

        Assert.Null(log["output"]);
        Assert.False((bool)log["timestamp"]!);
    }

    [Fact]
    public void Chosen_detail_level_reaches_the_engine() =>
        Assert.Equal("debug", (string?)Runtime(WithApp("debug"))!["log"]!["level"]);

    [Fact]
    public void Empty_level_falls_back_to_the_quiet_one() =>
        Assert.Equal("warn", (string?)Runtime(WithApp(" "))!["log"]!["level"]);
}
