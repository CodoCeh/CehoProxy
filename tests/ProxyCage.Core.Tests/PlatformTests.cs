using System.Text.Json.Nodes;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

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
    public void Only_the_chosen_apps_have_their_names_resolved_by_us()
    {
        // Запросы имён от остальной системы через нас не идут: там чужие программы,
        // и подменять им ответы мы не вправе. Исключение — запрос, присланный прямо
        // на наш адаптер: молчать в ответ значит подвесить систему.
        var hijack = Runtime()["route"]!["rules"]!.AsArray()
            .Where(r => (string?)r?["action"] == "hijack-dns").ToList();

        Assert.All(hijack, r => Assert.Equal("dns", (string?)r!["protocol"]));
        Assert.Single(hijack, r => r!["process_path_regex"] is not null);
        Assert.Single(hijack, r => (string?)r!["ip_cidr"]?[0] == new CehoConfig().TunAddress);
    }

    [Fact]
    public void Tun_options_match_the_running_system()
    {
        var tun = Runtime()["inbounds"]!.AsArray()
            .First(i => (string?)i!["type"] == "tun")!;

        Assert.True((bool?)tun["auto_route"]);
        Assert.Equal("gvisor", (string?)tun["stack"]);

        // Правил брандмауэра на всю машину не ставим ни на одной системе: они ломают
        // чужие туннели и локальную сеть, а нам для своих программ не нужны.
        Assert.Null(tun["strict_route"]);

        // Имя своё только на Linux: на Windows оно закрепляет GUID адаптера, а вместе с ним
        // и адрес прошлого запуска — движок потом не может его добавить.
        if (!Os.IsLinux) Assert.Null(tun["interface_name"]);

        if (Os.IsLinux)
        {
            Assert.Equal(TunCleanup.InterfaceName, (string?)tun["interface_name"]);
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
        Assert.NotEqual(2022, TunCleanup.Iproute2TableIndex);
        Assert.NotEqual(9000, TunCleanup.Iproute2RuleIndex);
    }

    [Fact]
    public void Timeout_setting_is_preserved_and_defaults_to_15()
    {
        var cfg = new CehoConfig();
        Assert.Equal(15, cfg.TimeoutSeconds);

        cfg.TimeoutSeconds = 45;
        var temp = Path.GetTempFileName();
        try
        {
            cfg.Save(temp);
            var loaded = CehoConfig.Load(temp);
            Assert.Equal(45, loaded.TimeoutSeconds);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    [Fact]
    public void Route_has_find_process_enabled_for_tun()
    {
        var app = new AppEntry
        {
            Name = "Cursor",
            Folder = @"C:\Users\s.bonich\AppData\Local\Programs\cursor",
        };
        var nodes = SubscriptionParser.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "sub-example.txt")));
        var cfg = new CehoConfig { Apps = { app } };

        var json = SingBoxConfigGenerator.GenerateForConfig(nodes, cfg);
        var root = System.Text.Json.Nodes.JsonNode.Parse(json)!;

        Assert.True((bool?)root["route"]!["find_process"]);
    }

    [Fact]
    public void Windows_app_regex_matches_nested_executables()
    {
        var app = new AppEntry
        {
            Name = "Cursor",
            Folder = @"C:\Users\s.bonich\AppData\Local\Programs\cursor",
        };
        var rx = AppDetector.ToRegex(app);

        Assert.Matches(rx, @"C:\Users\s.bonich\AppData\Local\Programs\cursor\Cursor.exe");
        Assert.Matches(rx, @"C:\Users\s.bonich\AppData\Local\Programs\cursor\resources\app\node.exe");
        Assert.DoesNotMatch(rx, @"C:\Users\s.bonich\AppData\Local\Programs\cursor-other\helper.exe");
    }

    [Fact]
    public void Windows_app_exe_path_matches_exe_and_directory()
    {
        var app = new AppEntry
        {
            Name = "Cursor",
            Folder = @"C:\Users\s.bonich\AppData\Local\Programs\cursor\Cursor.exe",
        };
        var rx = AppDetector.ToRegex(app);

        Assert.Matches(rx, @"C:\Users\s.bonich\AppData\Local\Programs\cursor\Cursor.exe");
        Assert.Matches(rx, @"C:\Users\s.bonich\AppData\Local\Programs\cursor\resources\app\node.exe");
        Assert.DoesNotMatch(rx, @"C:\Users\s.bonich\AppData\Local\Programs\cursor-other\helper.exe");
    }
}
