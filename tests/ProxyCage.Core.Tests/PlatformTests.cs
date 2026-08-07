using System.Text.Json.Nodes;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

/// <summary>
/// Платформенный шов. Тесты идут от текущей системы: правило изоляции и опции TUN
/// на Windows, Linux и macOS разные, и общего «правильного» ответа тут нет.
/// </summary>
public class PlatformTests
{
    private static string SystemBinDir => Os.Kind switch
    {
        OsKind.Windows => Environment.GetFolderPath(Environment.SpecialFolder.System),
        OsKind.Mac => "/usr/bin",
        _ => "/usr/bin",
    };

    private static string AppFolder => Os.IsWindows ? @"C:\Games\MyApp" : "/opt/myapp";

    [Fact]
    public void Isolating_a_whole_system_directory_is_refused()
    {
        // папка /usr/bin под правилом означала бы «изолировать половину системы»
        Assert.Throws<InvalidOperationException>(() => AppDetector.Detect(SystemBinDir));
    }

    [Fact]
    public void Program_inside_system_directory_falls_back_to_single_file()
    {
        var probe = Directory.EnumerateFiles(SystemBinDir).FirstOrDefault();
        Assert.NotNull(probe);

        var d = AppDetector.Detect(probe!);
        Assert.True(d.SingleFile);
        Assert.Equal(probe, d.Folder);

        var rx = AppDetector.ToRegex(new AppEntry { Folder = d.Folder, SingleFile = true });
        Assert.EndsWith("$", rx);
        Assert.Matches(rx, probe!);
    }

    [Fact]
    public void Folder_rule_matches_nested_processes_but_not_siblings()
    {
        var rx = AppDetector.ToRegex(new AppEntry { Folder = AppFolder });
        var sep = Os.IsWindows ? "\\" : "/";

        Assert.Matches(rx, $"{AppFolder}{sep}app{sep}helper");
        Assert.DoesNotMatch(rx, $"{AppFolder}-other{sep}helper");
        // сам путь папки без разделителя не считается процессом внутри неё
        Assert.DoesNotMatch(rx, AppFolder);
    }

    [Fact]
    public void Mac_bundle_is_isolated_whole()
    {
        if (!Os.IsMac) return;

        var d = AppDetector.Detect("/Applications/Safari.app/Contents/MacOS/Safari");
        Assert.EndsWith("Safari.app", d.Folder);
        Assert.False(d.SingleFile);
    }

    [Fact]
    public void Path_is_stored_as_the_system_really_sees_it()
    {
        if (Os.IsWindows) return;

        // Ядро отдаёт движку физический путь. Пример из жизни: /Applications/Safari.app —
        // символическая ссылка в Cryptex, а /tmp — ссылка на /private/tmp. Правило по
        // введённому пути не сработало бы МОЛЧА: продукт рапортует «изолировано»,
        // трафик идёт мимо. Поэтому храним то, что увидит система.
        var d = AppDetector.Detect("/Applications/Safari.app/Contents/MacOS/Safari");
        Assert.Equal(Os.RealPath(d.Folder), d.Folder);

        if (Os.IsMac && Directory.Exists("/private/tmp"))
            Assert.StartsWith("/private/tmp", Os.RealPath("/tmp"));
    }

    [Fact]
    public void Applications_folder_itself_is_refused()
    {
        if (!Os.IsMac) return;
        Assert.Throws<InvalidOperationException>(() => AppDetector.Detect("/Applications"));
    }

    private static CehoConfig ConfigWithApp() => new()
    {
        Apps = { new AppEntry { Name = "app", Folder = AppFolder } },
    };

    private static JsonNode Runtime()
    {
        var nodes = SubscriptionParser.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "sub-example.txt")));
        return JsonNode.Parse(SingBoxConfigGenerator.GenerateForConfig(nodes, ConfigWithApp()))!;
    }

    [Fact]
    public void Everything_outside_the_rule_stays_direct()
    {
        var root = Runtime();
        Assert.Equal("direct", (string?)root["route"]!["final"]);
    }

    [Fact]
    public void System_dns_is_hijacked_not_routed()
    {
        // без этого правила sing-box пытается direct-ом достучаться до адреса внутри
        // собственной подсети TUN, и резолвинг ложится у ВСЕЙ машины
        var rules = Runtime()["route"]!["rules"]!.AsArray();
        Assert.Contains(rules, r => (string?)r?["action"] == "hijack-dns");
    }

    [Fact]
    public void Tun_options_match_the_running_system()
    {
        var tun = Runtime()["inbounds"]!.AsArray()
            .First(i => (string?)i!["type"] == "tun")!;

        Assert.True((bool?)tun["auto_route"]);
        Assert.Equal("gvisor", (string?)tun["stack"]);

        if (Os.IsMac)
            Assert.Null(tun["strict_route"]);       // на macOS опции нет
        else
            Assert.True((bool?)tun["strict_route"]);

        if (Os.IsLinux)
        {
            Assert.Equal(TunCleanup.LinuxInterfaceName, (string?)tun["interface_name"]);
            Assert.Equal(TunCleanup.Iproute2TableIndex, (int?)tun["iproute2_table_index"]);
            Assert.Equal(TunCleanup.Iproute2RuleIndex, (int?)tun["iproute2_rule_index"]);
        }
        else
        {
            Assert.Null(tun["iproute2_table_index"]);
        }
    }

    [Fact]
    public void Our_iproute2_indices_differ_from_sing_box_defaults()
    {
        // на этом держится самолечение: чистим только СВОЙ мусор и не сносим
        // чужой туннель, который может работать на той же машине
        Assert.NotEqual(2022, TunCleanup.Iproute2TableIndex);
        Assert.NotEqual(9000, TunCleanup.Iproute2RuleIndex);
    }
}
