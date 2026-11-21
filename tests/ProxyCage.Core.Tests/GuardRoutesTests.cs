using System.Text.Json.Nodes;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class GuardRoutesTests
{
    private const string Ifconfig =
        "utun6: flags=8051<UP,POINTOPOINT,RUNNING,MULTICAST> mtu 1380\n" +
        "\tinet6 fe80::5b58:b4d8:1d73:56e9%utun6 prefixlen 64 scopeid 0x1b \n" +
        "utun7: flags=8051<UP,POINTOPOINT,RUNNING,MULTICAST> mtu 65535\n" +
        "\tinet 172.31.211.5 --> 172.31.211.5 netmask 0xfffffffc\n" +
        "\tinet6 fdce:b5:211::5 prefixlen 126\n" +
        "utun8: flags=8051<UP,POINTOPOINT,RUNNING,MULTICAST> mtu 65535\n" +
        "\tinet 172.31.211.1 --> 172.31.211.1 netmask 0xfffffffc\n";

    [Fact]
    public void Finds_the_adapter_that_carries_the_guard_address()
    {
        Assert.Equal("utun7", GuardRoutes.FindInterface(Ifconfig, "172.31.211.5"));
        Assert.Equal("utun7", GuardRoutes.FindInterface(Ifconfig, CehoConfig.GuardTunAddress));
        Assert.Equal("utun7", GuardRoutes.FindInterface(Ifconfig, CehoConfig.GuardTunAddress6));
        Assert.Equal("utun8", GuardRoutes.FindInterface(Ifconfig, "172.31.211.1"));
        Assert.Equal("utun7", GuardRoutes.FindInterface(Ifconfig, "fdce:b5:211::5"));
        Assert.Null(GuardRoutes.FindInterface(Ifconfig, "172.31.211.9"));
    }

    [Fact]
    public void Reads_only_static_ipv6_prefixes_of_the_guard_adapter()
    {
        const string netstat =
            "default                                 fe80::%utun6                            UGcIg               utun6\n" +
            "fe80::%utun7/64                         fe80::1%utun7                           UcI                 utun7\n" +
            "ff00::/8                                fe80::1%utun7                           UmCI                utun7\n" +
            "2000::/3                                fdce:b5:211::5                          UGSc                utun7\n" +
            "fdce:b5:211::5                          fdce:b5:211::5                          UH                  utun7\n" +
            "8000::/1                                fdce:b5:211::1                          UGSc                utun8\n";
        Assert.Equal(["2000::/3"], GuardRoutes.ParseV6Prefixes(netstat, "utun7"));
    }

    [Fact]
    public void Builds_one_route_command_per_prefix()
    {
        var commands = GuardRoutes.Commands("utun7", ["2000::/3"]);
        Assert.Equal(GuardRoutes.V4Prefixes.Length + 1, commands.Count);
        Assert.Contains("-n add -net 1.0.0.0/8 -interface utun7", commands);
        Assert.Contains("-n add -net 128.0.0.0/1 -interface utun7", commands);
        Assert.Contains("-n add -inet6 2000::/3 -interface utun7", commands);
        Assert.All(GuardRoutes.V4Prefixes, p => Assert.Contains($"-n add -net {p} -interface utun7", commands));
    }

    private static JsonObject Tun(string json) =>
        JsonNode.Parse(json)!["inbounds"]!.AsArray().Select(i => i!.AsObject()).Single(i => (string?)i["type"] == "tun");

    private static CehoConfig Config() => new() { Apps = { new AppEntry { Name = "A", Folder = "/Applications/A.app", Enabled = true } } };

    [Fact]
    public void The_guard_takes_routes_by_default_and_can_wait_without_them()
    {
        Assert.True((bool)Tun(SingBoxConfigGenerator.GenerateFailClosed(Config()))["auto_route"]!);
        Assert.False((bool)Tun(SingBoxConfigGenerator.GenerateFailClosed(Config(), null, autoRoute: false))["auto_route"]!);
    }

    [Fact]
    public void The_guard_log_can_go_to_a_file_so_a_closed_pipe_cannot_kill_it()
    {
        var withFile = JsonNode.Parse(SingBoxConfigGenerator.GenerateFailClosed(Config(), null, true, "/tmp/guard.log"))!["log"]!;
        Assert.Equal("/tmp/guard.log", (string?)withFile["output"]);
        var without = JsonNode.Parse(SingBoxConfigGenerator.GenerateFailClosed(Config()))!["log"]!;
        Assert.Null(without["output"]);
    }

    [Fact]
    public void Launchd_keeps_children_alive_when_the_service_restarts()
    {
        var plist = Autostart.LaunchdPlist("/x/cehoproxy", "/x");
        Assert.Contains("<key>AbandonProcessGroup</key><true/>", plist);
        var old = plist.Replace("  <key>AbandonProcessGroup</key><true/>\n", "").Replace("<key>AbandonProcessGroup</key><true/>", "");
        var patched = Autostart.AddLaunchdAbandonGroup(old);
        Assert.Contains("<key>AbandonProcessGroup</key><true/>", patched);
        Assert.Equal(patched, Autostart.AddLaunchdAbandonGroup(patched));
    }

    [Fact]
    public void Keep_guard_marker_expires()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-kg-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            Assert.False(DaemonControl.KeepGuardRequested(root));
            DaemonControl.MarkKeepGuard(root);
            Assert.True(DaemonControl.KeepGuardRequested(root));
            File.SetLastWriteTimeUtc(DaemonControl.KeepGuardMarker(root), DateTime.UtcNow.AddMinutes(-10));
            Assert.False(DaemonControl.KeepGuardRequested(root));
            DaemonControl.MarkKeepGuard(root);
            DaemonControl.ClearKeepGuard(root);
            Assert.False(DaemonControl.KeepGuardRequested(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Daemon_wiring_never_drops_the_guard_before_the_engine_is_up_on_mac()
    {
        var source = Source("src/ProxyCage.Cli/Program.cs");
        var start = source.IndexOf("var p = new SingBoxProcess();", StringComparison.Ordinal);
        var tail = source[start..source.IndexOf("string? StopTunnel()", start, StringComparison.Ordinal)];
        Assert.Contains("if (!Os.IsMac) StopGuard();", tail);
        Assert.DoesNotContain("            StopGuard();\n            p.Start", tail.Replace("\r\n", "\n"));
        Assert.Contains("p.Exited += () => EngageGuardAfterExit(p);", tail);

        var stop = source[source.IndexOf("string? StopTunnelLocked()", StringComparison.Ordinal)..];
        stop = stop[..stop.IndexOf("var web = new WebServer", StringComparison.Ordinal)];
        Assert.Contains("GuardRoutes.Engage(Log.Info)", stop);

        var shutdown = source[source.IndexOf("DaemonControl.WaitForStop();", StringComparison.Ordinal)..];
        Assert.Contains("DaemonControl.KeepGuardRequested(Ceho.Root)", shutdown);
    }

    private static string Source(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ProxyCage.sln"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, relative));
    }
}
