using System.Diagnostics;
using System.Text.Json.Nodes;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class EngineAcceptsSubscriptionsTests
{
    public static IEnumerable<object[]> Fixtures() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures"), "sub-*")
            .Where(f => !f.EndsWith("sub-hwid-gate.txt"))
            .Select(f => new object[] { Path.GetFileName(f) });

    private static Dictionary<string, JsonObject> Transports() =>
        SubscriptionParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "sub-transports.txt")))
            .ToDictionary(n => n.Remark, n => OutboundBuilder.Build(n));

    [Fact]
    public void Every_common_transport_keeps_its_settings()
    {
        var o = Transports();
        Assert.Equal("httpupgrade", (string?)o["vless-httpupgrade"]["transport"]!["type"]);
        Assert.Equal("cdn.example", (string?)o["vless-httpupgrade"]["transport"]!["host"]);
        Assert.Equal("http", (string?)o["vless-h2"]["transport"]!["type"]);
        Assert.Equal("/ws", (string?)o["vless-ws-ed"]["transport"]!["path"]);
        Assert.Equal(2048, (int)o["vless-ws-ed"]["transport"]!["max_early_data"]!);
        Assert.Equal("obfs-local", (string?)o["ss-obfs"]["plugin"]);
        Assert.Equal("obfs=http;obfs-host=bing.com", (string?)o["ss-obfs"]["plugin_opts"]);
        Assert.Equal("v2ray-plugin", (string?)o["ss-v2ray"]["plugin"]);
        Assert.Equal("anytls", (string?)o["anytls"]["type"]);
        Assert.Equal("httpupgrade", (string?)o["vmess-httpupgrade"]["transport"]!["type"]);
        Assert.False(o.ContainsKey("vless-xhttp"));
    }

    [Fact]
    public void Subscription_of_only_xhttp_servers_is_explained()
    {
        const string xhttp = "vless://11111111-1111-1111-1111-111111111111@198.51.100.4:443?security=reality&type=xhttp&path=%2Fx#X";
        Assert.Empty(SubscriptionParser.Parse(xhttp));
        Assert.True(SubscriptionParser.OnlyUnsupportedTransports(xhttp));
        Assert.Null(SubscriptionParser.ProviderNotice(xhttp));
        Assert.False(SubscriptionParser.OnlyUnsupportedTransports(xhttp + "\nvless://11111111-1111-1111-1111-111111111111@198.51.100.5:443?type=tcp#T"));
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Every_node_of_a_subscription_is_accepted_by_sing_box(string fixture)
    {
        var engine = Environment.GetEnvironmentVariable("CEHO_SINGBOX_CHECK");
        if (string.IsNullOrEmpty(engine) || !File.Exists(engine)) return;

        var nodes = SubscriptionParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", fixture)));
        Assert.NotEmpty(nodes);
        var failures = new List<string>();
        foreach (var node in nodes)
        {
            var outbound = OutboundBuilder.Build(node);
            outbound["tag"] = "x";
            var config = Path.Combine(Path.GetTempPath(), $"ceho-node-{Guid.NewGuid():N}.json");
            File.WriteAllText(config, new JsonObject
            {
                ["dns"] = new JsonObject { ["servers"] = new JsonArray(new JsonObject { ["type"] = "local", ["tag"] = "dns-direct" }) },
                ["outbounds"] = new JsonArray(outbound),
            }.ToJsonString());
            try
            {
                var run = Process.Start(new ProcessStartInfo(engine, $"check -c \"{config}\"")
                    { RedirectStandardError = true, RedirectStandardOutput = true })!;
                var error = run.StandardError.ReadToEnd();
                run.WaitForExit();
                if (run.ExitCode != 0) failures.Add($"{node.Protocol} {node.Network} «{node.Remark}»: {error.Trim()}");
            }
            finally { File.Delete(config); }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
