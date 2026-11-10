using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class UninstallModesTests
{
    private static string FakeRoot()
    {
        var root = Path.Combine(Directory.CreateTempSubdirectory().FullName, "CehoProxy");
        Directory.CreateDirectory(Path.Combine(root, "country-rules"));
        Directory.CreateDirectory(Path.Combine(root, "geoip"));
        var cfg = new CehoConfig();
        cfg.Subscriptions.Add(new SubscriptionEntry { Name = "main", Url = "https://s.example/a" });
        cfg.Apps.Add(new AppEntry { Name = "x", Folder = "/tmp/x" });
        cfg.Save(Path.Combine(root, "config.json"));
        File.WriteAllText(Path.Combine(root, "sub-main.txt"), "vless://u@h:1");
        foreach (var name in new[] { "cehoproxy", "singbox.json", "engine.lock", "cehoproxy.log", "unknown-new-file.bin" })
            File.WriteAllText(Path.Combine(root, name), "x");
        return root;
    }

    [Fact]
    public void Full_uninstall_leaves_nothing_in_our_folder()
    {
        if (Os.IsWindows) return;
        var root = FakeRoot();
        Installer.FinishUninstall(root, keep: false, _ => { });
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Keeping_settings_leaves_only_them_and_a_new_install_can_bring_them_back()
    {
        if (Os.IsWindows) return;
        var root = FakeRoot();
        SavedSettings.Save(root);
        Installer.FinishUninstall(root, keep: true, _ => { });
        Assert.Equal(new[] { SavedSettings.FolderName }, Directory.GetFileSystemEntries(root).Select(Path.GetFileName));
        Assert.Equal((1, 1), SavedSettings.Summary(root));

        new CehoConfig().Save(Path.Combine(root, "config.json"));
        SavedSettings.Restore(root);
        var cfg = CehoConfig.Load(Path.Combine(root, "config.json"));
        Assert.Single(cfg.Subscriptions);
        Assert.True(File.Exists(Path.Combine(root, "sub-main.txt")));
        Assert.False(SavedSettings.Exists(root));
    }

    [Fact]
    public void A_folder_that_is_not_ours_is_never_emptied_wholesale()
    {
        if (Os.IsWindows) return;
        var root = Directory.CreateTempSubdirectory("not-ours").FullName;
        File.WriteAllText(Path.Combine(root, "someone-else.txt"), "keep me");
        Installer.FinishUninstall(root, keep: false, _ => { });
        Assert.True(File.Exists(Path.Combine(root, "someone-else.txt")));
    }
}
