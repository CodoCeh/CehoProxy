using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class InstallerCleanupTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "chp-install-" + Guid.NewGuid().ToString("N")[..8]);

    public InstallerCleanupTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private void Touch(string name, string text = "x")
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private bool Exists(string name) => File.Exists(Path.Combine(_root, name));

    [Fact]
    public void Old_version_files_go_away_and_user_data_stays()
    {
        Touch("config.json", "{\"Lang\":\"ru\"}");
        Touch("sub-1.txt", "vless://one");
        Touch("sub-2.txt", "vless://two");
        Touch("cehoproxy.exe.old");
        Touch("cehoproxy.new");
        Touch("singbox.json");
        Touch("panel.port", "8777");
        Touch("cehoproxy.pid", "4242");
        Touch("tun-devices.txt", "SWD\\Wintun\\{old}");
        Touch("sing-box-1.9.0-windows-amd64.zip");
        Touch("engine-tmp/sing-box.exe");

        var (wiped, kept) = Installer.WipeVersionLeftovers(_root);

        Assert.Equal("{\"Lang\":\"ru\"}", File.ReadAllText(Path.Combine(_root, "config.json")));
        Assert.True(Exists("sub-1.txt") && Exists("sub-2.txt"));
        Assert.Equal(3, kept);

        Assert.False(Exists("cehoproxy.exe.old"));
        Assert.False(Exists("cehoproxy.new"));
        Assert.False(Exists("singbox.json"));
        Assert.False(Exists("panel.port"));
        Assert.False(Exists("cehoproxy.pid"));
        Assert.False(Exists("tun-devices.txt"));
        Assert.False(Exists("sing-box-1.9.0-windows-amd64.zip"));
        Assert.False(Directory.Exists(Path.Combine(_root, "engine-tmp")));
        Assert.Equal(8, wiped);
    }

    [Fact]
    public void Journal_and_engine_binary_survive_the_update()
    {
        Touch("cehoproxy.log", "старые записи");
        Touch("sing-box.exe", "движок");
        Touch("ceho-engine.exe", "свой движок");
        Touch("chp.cmd", "@echo off");

        Installer.WipeVersionLeftovers(_root);

        Assert.Equal("старые записи", File.ReadAllText(Path.Combine(_root, "cehoproxy.log")));
        Assert.True(Exists("sing-box.exe"));
        Assert.True(Exists("ceho-engine.exe"));
        Assert.True(Exists("chp.cmd"));
    }

    [Fact]
    public void Separate_files_of_the_old_journal_scheme_are_swept_away()
    {
        Touch("sing-box.log", "лог движка от прошлой версии");
        Touch("sing-box.log.1");
        Touch("crash-20260907-101500.log", "падение");
        Touch("crash-20260906-090000.log", "падение");

        var (wiped, _) = Installer.WipeVersionLeftovers(_root);

        Assert.False(Exists("sing-box.log"));
        Assert.False(Exists("sing-box.log.1"));
        Assert.False(Exists("crash-20260907-101500.log"));
        Assert.False(Exists("crash-20260906-090000.log"));
        Assert.Equal(4, wiped);
    }

    [Fact]
    public void Nothing_to_clean_is_not_an_error()
    {
        var (wiped, kept) = Installer.WipeVersionLeftovers(_root);

        Assert.Equal(0, wiped);
        Assert.Equal(0, kept);
    }
}
