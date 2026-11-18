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
    }

    [Fact]
    public void Vless_xhttp_goes_through_the_xray_bridge()
    {
        var node = SubscriptionParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "sub-transports.txt")))
            .Single(n => n.Remark == "vless-xhttp");
        node.BridgePort = 20808;

        var outbound = OutboundBuilder.Build(node);
        Assert.Equal("socks", (string?)outbound["type"]);
        Assert.Equal("127.0.0.1", (string?)outbound["server"]);
        Assert.Equal(20808, (int)outbound["server_port"]!);

        var xray = JsonNode.Parse(XrayBridge.Config(new[] { node }))!;
        Assert.Equal(20808, (int)xray["inbounds"]![0]!["port"]!);
        var stream = xray["outbounds"]![0]!["streamSettings"]!;
        Assert.Equal("xhttp", (string?)stream["network"]);
        Assert.Equal("reality", (string?)stream["security"]);
        Assert.Equal("Z84J2IelR9ch3k8VtlVhhs5ycBUlXA7wHBWcBrjqnAw", (string?)stream["realitySettings"]!["publicKey"]);
        Assert.Equal("/x", (string?)stream["xhttpSettings"]!["path"]);
        Assert.Equal("auto", (string?)stream["xhttpSettings"]!["mode"]);
    }

    [Fact]
    public async Task Bridge_ports_stay_the_same_while_the_bridge_holds_them()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-bridge-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        File.WriteAllText(XrayBridge.BinaryPath(root), "");
        try
        {
            const string link = "vless://11111111-1111-1111-1111-111111111111@198.51.100.4:443?security=none&type=xhttp&path=%2Fx#X";
            var first = (await XrayBridge.PrepareAsync(SubscriptionParser.Parse(link), root, _ => { })).Single().BridgePort!.Value;
            using var held = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, first);
            held.Start();
            var second = (await XrayBridge.PrepareAsync(SubscriptionParser.Parse(link), root, _ => { })).Single().BridgePort!.Value;
            Assert.Equal(first, second);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Without_xhttp_servers_the_bridge_config_is_removed()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-bridge-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        File.WriteAllText(XrayBridge.ConfigPath(root), "{}");
        try
        {
            var nodes = await XrayBridge.PrepareAsync(SubscriptionParser.Parse("vless://11111111-1111-1111-1111-111111111111@198.51.100.5:443?type=tcp#T"), root, _ => { });
            Assert.Null(nodes.Single().BridgePort);
            Assert.False(File.Exists(XrayBridge.ConfigPath(root)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Xhttp_extra_settings_are_passed_to_xray()
    {
        var node = SubscriptionParser.Parse("vless://11111111-1111-1111-1111-111111111111@198.51.100.4:443?security=tls&sni=x.example&type=xhttp&path=%2Fx&mode=packet-up&extra=%7B%22xPaddingBytes%22%3A%22100-1000%22%7D#X").Single();
        node.BridgePort = 20809;
        var xhttp = JsonNode.Parse(XrayBridge.Config(new[] { node }))!["outbounds"]![0]!["streamSettings"]!["xhttpSettings"]!;
        Assert.Equal("packet-up", (string?)xhttp["mode"]);
        Assert.Equal("100-1000", (string?)xhttp["extra"]!["xPaddingBytes"]);
    }

    [Fact]
    public void Only_non_vless_xhttp_is_explained_as_unsupported()
    {
        const string vless = "vless://11111111-1111-1111-1111-111111111111@198.51.100.4:443?security=reality&type=xhttp&path=%2Fx#X";
        const string trojan = "trojan://secret@198.51.100.6:443?security=tls&type=xhttp&path=%2Fx#T";
        Assert.Single(SubscriptionParser.Parse(vless));
        Assert.False(SubscriptionParser.OnlyUnsupportedTransports(vless));
        Assert.Empty(SubscriptionParser.Parse(trojan));
        Assert.True(SubscriptionParser.OnlyUnsupportedTransports(trojan));
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
            if (XrayBridge.Supports(node)) node.BridgePort = 20808;
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
