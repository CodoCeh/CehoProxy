using System.Text.Json.Nodes;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class FailClosedTests
{
    private static string Folder(string name) =>
        Os.IsWindows ? $@"C:\Games\{name}" : $"/tmp/{name}";

    private static CehoConfig TwoApps() => new()
    {
        Apps =
        {
            new AppEntry { Name = "general", Folder = Folder("General") },
            new AppEntry { Name = "second", Folder = Folder("Second") },
        },
    };

    private static JsonNode Guard(CehoConfig cfg) =>
        JsonNode.Parse(SingBoxConfigGenerator.GenerateFailClosed(cfg))!;

    private static IReadOnlyList<ProxyNode> Nodes() =>
        SubscriptionParser.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "sub-example.txt")));

    [Fact]
    public void Isolated_programs_are_rejected_not_sent_out_directly()
    {
        var rules = Guard(TwoApps())["route"]!["rules"]!.AsArray();
        var reject = rules.First(r => (string?)r?["action"] == "reject");

        var regexes = reject["process_path_regex"]!.AsArray().Select(x => (string)x!).ToList();
        Assert.All(
            new[] { Folder("General"), Folder("Second") },
            folder => Assert.Contains(regexes, rx =>
                System.Text.RegularExpressions.Regex.IsMatch(
                    Path.Combine(folder, "app.bin"), rx)));
    }

    [Fact]
    public void Everything_else_keeps_going_out_directly()
    {
        var route = Guard(TwoApps())["route"]!;
        Assert.Equal("direct", (string?)route["final"]);
        Assert.True((bool?)route["auto_detect_interface"]);
    }

    [Fact]
    public void Blocking_tunnel_has_no_way_out_through_a_node()
    {
        var root = Guard(TwoApps());
        var outbounds = root["outbounds"]!.AsArray();

        Assert.Single(outbounds);
        Assert.Equal("direct", (string?)outbounds[0]!["type"]);
        Assert.DoesNotContain(root["route"]!["rules"]!.AsArray(), r => (string?)r?["outbound"] == "proxy");
    }

    [Fact]
    public void Our_own_traffic_leaves_before_the_ban_applies()
    {
        var rules = Guard(TwoApps())["route"]!["rules"]!.AsArray();
        var direct = rules.ToList().FindIndex(r => (string?)r?["outbound"] == "direct");
        var reject = rules.ToList().FindIndex(r => (string?)r?["action"] == "reject");

        Assert.True(direct >= 0);
        Assert.True(direct < reject);
    }

    [Fact]
    public void Blocking_tunnel_lives_on_its_own_address()
    {
        var cfg = TwoApps();
        var tun = Guard(cfg)["inbounds"]!.AsArray().First(i => (string?)i!["type"] == "tun")!;
        var address = tun["address"]!.AsArray().Select(x => (string)x!).ToList();

        Assert.Contains(CehoConfig.GuardTunAddress, address);
        Assert.DoesNotContain(cfg.TunAddress, address);
    }

    [Fact]
    public void Blocking_tunnel_keeps_the_platform_choices_of_the_working_one()
    {
        var tun = Guard(TwoApps())["inbounds"]!.AsArray().First(i => (string?)i!["type"] == "tun")!;

        Assert.True((bool?)tun["auto_route"]);
        Assert.Null(tun["strict_route"]);
        Assert.Equal(Os.IsWindows ? "system" : "gvisor", (string?)tun["stack"]);

        if (Os.IsMac) Assert.Null(tun["interface_name"]);
        else Assert.Equal(TunCleanup.InterfaceName, (string?)tun["interface_name"]);

        if (Os.IsLinux)
        {
            Assert.Equal(TunCleanup.Iproute2TableIndex, (int?)tun["iproute2_table_index"]);
            Assert.Equal(TunCleanup.Iproute2RuleIndex, (int?)tun["iproute2_rule_index"]);
        }
    }

    [Fact]
    public void Without_isolated_programs_there_is_nothing_to_block()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SingBoxConfigGenerator.GenerateFailClosed(new CehoConfig()));
    }

    [Fact]
    public void Disabled_program_is_not_blocked()
    {
        var cfg = TwoApps();
        cfg.Apps[1].Enabled = false;

        var reject = Guard(cfg)["route"]!["rules"]!.AsArray()
            .First(r => (string?)r?["action"] == "reject");
        var regexes = reject["process_path_regex"]!.AsArray().Select(x => (string)x!).ToList();

        Assert.DoesNotContain(regexes, rx =>
            System.Text.RegularExpressions.Regex.IsMatch(Path.Combine(Folder("Second"), "app.bin"), rx));
    }

    [Fact]
    public void Fail_closed_is_on_out_of_the_box_and_survives_saving()
    {
        var cfg = new CehoConfig();
        Assert.True(cfg.FailClosed);

        cfg.FailClosed = false;
        var temp = Path.GetTempFileName();
        try
        {
            cfg.Save(temp);
            Assert.False(CehoConfig.Load(temp).FailClosed);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    [Fact]
    public void Guard_state_file_is_what_the_panel_reads()
    {
        if (Os.IsWindows) return;

        var root = Path.Combine(Path.GetTempPath(), "ceho-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.False(LeakGuard.IsActive(root));
            LeakGuard.SetTunnelGuard(root, true);
            Assert.True(LeakGuard.IsActive(root));
            LeakGuard.SetTunnelGuard(root, false);
            Assert.False(LeakGuard.IsActive(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Guard_rules_live_next_to_the_working_rules()
    {
        var root = Os.IsWindows ? @"C:\ProgramData\CehoProxy" : "/etc/cehoproxy";
        Assert.Equal(Path.Combine(root, "guard.json"), TunCleanup.GuardConfigPath(root));
    }

    [Fact]
    public void Removing_the_guard_clears_its_mark_on_every_system()
    {
        if (Os.IsWindows) return;

        var root = Path.Combine(Path.GetTempPath(), "ceho-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            LeakGuard.SetTunnelGuard(root, true);
            LeakGuard.Remove(root);
            Assert.False(LeakGuard.IsActive(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Tunnel_takes_an_ipv6_address_so_ipv6_cannot_slip_past_it()
    {
        var cfg = TwoApps();
        Assert.True(cfg.TunIpv6);

        var working = JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(Nodes(), cfg))!;
        var tun = working["inbounds"]!.AsArray().First(i => (string?)i!["type"] == "tun")!;
        var address = tun["address"]!.AsArray().Select(x => (string)x!).ToList();

        Assert.Equal(
            Os.IsWindows
                ? new List<string> { cfg.TunAddress }
                : new List<string> { cfg.TunAddress, CehoConfig.TunAddress6 },
            address);
    }

    [Fact]
    public void Ipv6_can_be_switched_off_and_then_the_tunnel_is_ipv4_only()
    {
        var cfg = TwoApps();
        cfg.TunIpv6 = false;

        var working = JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(Nodes(), cfg))!;
        var tun = working["inbounds"]!.AsArray().First(i => (string?)i!["type"] == "tun")!;
        Assert.Equal(new List<string> { cfg.TunAddress }, tun["address"]!.AsArray().Select(x => (string)x!).ToList());

        var guardTun = Guard(cfg)["inbounds"]!.AsArray().First(i => (string?)i!["type"] == "tun")!;
        Assert.Equal(
            new List<string> { CehoConfig.GuardTunAddress },
            guardTun["address"]!.AsArray().Select(x => (string)x!).ToList());
    }

    [Fact]
    public void Blocking_tunnel_takes_ipv6_too()
    {
        var guardTun = Guard(TwoApps())["inbounds"]!.AsArray().First(i => (string?)i!["type"] == "tun")!;
        Assert.Equal(
            Os.IsWindows
                ? new List<string> { CehoConfig.GuardTunAddress }
                : new List<string> { CehoConfig.GuardTunAddress, CehoConfig.GuardTunAddress6 },
            guardTun["address"]!.AsArray().Select(x => (string)x!).ToList());
    }

    [Fact]
    public void Name_questions_asked_to_our_adapter_are_answered_on_both_families()
    {
        var cfg = TwoApps();
        var rules = JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(Nodes(), cfg))!
            ["route"]!["rules"]!.AsArray();
        var hijack = rules.First(r => (string?)r?["action"] == "hijack-dns" && r["ip_cidr"] is not null);

        Assert.Equal(
            Os.IsWindows
                ? new List<string> { cfg.TunAddress }
                : new List<string> { cfg.TunAddress, CehoConfig.TunAddress6 },
            hijack["ip_cidr"]!.AsArray().Select(x => (string)x!).ToList());
    }

    [Fact]
    public void Our_ipv6_address_is_not_the_factory_one_of_the_engine()
    {
        Assert.DoesNotContain("fdfe", CehoConfig.TunAddress6, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fdfe", CehoConfig.GuardTunAddress6, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("fd", CehoConfig.TunAddress6, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(CehoConfig.TunAddress6, CehoConfig.GuardTunAddress6);
    }
}
