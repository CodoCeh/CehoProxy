using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class SettingsTransferTests
{
    private static string Machine(int webPort, int mixedPort, string app, string sub)
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var cfg = new CehoConfig { WebPort = webPort, MixedPort = mixedPort };
        cfg.Apps.Add(new AppEntry { Name = app, Folder = "/opt/" + app });
        cfg.Save(Path.Combine(root, "config.json"));
        File.WriteAllText(Path.Combine(root, $"sub-{sub}.txt"), "vless://node-" + sub);
        return root;
    }

    [Fact]
    public void Settings_move_to_another_computer_but_its_ports_stay()
    {
        var from = Machine(8899, 2080, "telegram", "main");
        var to = Machine(8900, 2084, "other", "old");
        try
        {
            var file = SettingsTransfer.Export(from, "secret-1");
            Assert.StartsWith(SettingsTransfer.Header, file);
            Assert.DoesNotContain("vless://", file);
            Assert.DoesNotContain("telegram", file);

            var result = SettingsTransfer.Import(to, file, "secret-1", resolveApp: a => a);
            var moved = CehoConfig.Load(Path.Combine(to, "config.json"));

            Assert.Equal(1, result.SubFiles);
            Assert.Contains(moved.Apps, a => a.Name == "telegram");
            Assert.Equal(8900, moved.WebPort);
            Assert.Equal(2084, moved.MixedPort);
            Assert.Equal("vless://node-main", File.ReadAllText(Path.Combine(to, "sub-main.txt")));
        }
        finally { Directory.Delete(from, true); Directory.Delete(to, true); }
    }

    [Fact]
    public void Wrong_password_changes_nothing()
    {
        var from = Machine(8899, 2080, "telegram", "main");
        var to = Machine(8900, 2084, "other", "old");
        try
        {
            var file = SettingsTransfer.Export(from, "secret-1");
            Assert.Throws<SettingsTransfer.WrongPasswordException>(() => SettingsTransfer.Import(to, file, "secret-2"));
            Assert.Equal("other", Assert.Single(CehoConfig.Load(Path.Combine(to, "config.json")).Apps).Name);
            Assert.Throws<InvalidDataException>(() => SettingsTransfer.Import(to, "hello", "secret-1"));
        }
        finally { Directory.Delete(from, true); Directory.Delete(to, true); }
    }

    [Fact]
    public void Only_chosen_parts_leave_and_arrive()
    {
        var from = Machine(8899, 2080, "telegram", "main");
        var cfg = CehoConfig.Load(Path.Combine(from, "config.json"));
        cfg.DirectSites.Add("bank.example");
        cfg.Subscriptions.Add(new SubscriptionEntry { Name = "main", Url = "https://sub.example/x" });
        cfg.Save(Path.Combine(from, "config.json"));
        var to = Machine(8900, 2084, "other", "old");
        try
        {
            var sitesOnly = SettingsTransfer.Export(from, "secret-1", SettingsTransfer.Parts.Sites);
            var r1 = SettingsTransfer.Import(to, sitesOnly, "secret-1");
            var after = CehoConfig.Load(Path.Combine(to, "config.json"));
            Assert.Equal(SettingsTransfer.Parts.Sites, r1.Applied);
            Assert.Equal("bank.example", Assert.Single(after.DirectSites));
            Assert.Empty(after.Subscriptions);
            Assert.Equal("other", Assert.Single(after.Apps).Name);
            Assert.False(File.Exists(Path.Combine(to, "sub-main.txt")));

            var all = SettingsTransfer.Export(from, "secret-1");
            var r2 = SettingsTransfer.Import(to, all, "secret-1", SettingsTransfer.Parts.Vpn);
            after = CehoConfig.Load(Path.Combine(to, "config.json"));
            Assert.Equal(SettingsTransfer.Parts.Vpn, r2.Applied);
            Assert.Equal("main", Assert.Single(after.Subscriptions).Name);
            Assert.Equal("other", Assert.Single(after.Apps).Name);
            Assert.True(File.Exists(Path.Combine(to, "sub-main.txt")));

            var r3 = SettingsTransfer.Import(to, sitesOnly, "secret-1", SettingsTransfer.Parts.Vpn);
            Assert.Equal(SettingsTransfer.Parts.None, r3.Applied);
        }
        finally { Directory.Delete(from, true); Directory.Delete(to, true); }
    }

    [Fact]
    public void Apps_are_added_when_found_and_reported_when_not()
    {
        var from = Machine(8899, 2080, "telegram", "main");
        var cfg = CehoConfig.Load(Path.Combine(from, "config.json"));
        cfg.Apps.Add(new AppEntry { Name = "ghost", Folder = "/nowhere/ghost" });
        cfg.Apps.Add(new AppEntry { Name = "moved", Folder = "/old/place/moved" });
        cfg.Save(Path.Combine(from, "config.json"));
        var to = Machine(8900, 2084, "other", "old");
        var here = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var file = SettingsTransfer.Export(from, "secret-1", SettingsTransfer.Parts.Apps);
            var result = SettingsTransfer.Import(to, file, "secret-1", SettingsTransfer.Parts.All, a =>
                a.Name == "telegram" ? new AppEntry { Name = a.Name, Folder = here }
                : a.Name == "moved" ? new AppEntry { Name = a.Name, Folder = here + "/m" }
                : null);
            var after = CehoConfig.Load(Path.Combine(to, "config.json"));
            Assert.Equal(new[] { "telegram", "moved" }, result.AppsAdded);
            Assert.Equal(new[] { "ghost" }, result.AppsMissing);
            Assert.Equal(3, after.Apps.Count);
            Assert.Contains("ghost", SettingsTransfer.Describe(result, "ru"));
        }
        finally { Directory.Delete(from, true); Directory.Delete(to, true); Directory.Delete(here, true); }
    }

    [Theory]
    [InlineData("", SettingsTransfer.Parts.All)]
    [InlineData("все", SettingsTransfer.Parts.All)]
    [InlineData("vpn", SettingsTransfer.Parts.Vpn)]
    [InlineData("программы,сайты", SettingsTransfer.Parts.Apps | SettingsTransfer.Parts.Sites)]
    [InlineData("1 3", SettingsTransfer.Parts.Vpn | SettingsTransfer.Parts.Sites)]
    [InlineData("мусор", SettingsTransfer.Parts.None)]
    public void Parts_are_read_from_words_and_numbers(string text, SettingsTransfer.Parts expected) =>
        Assert.Equal(expected, SettingsTransfer.ParseParts(text));

    [Theory]
    [InlineData("sub-main.txt", true)]
    [InlineData("sub-../../etc/passwd.txt", false)]
    [InlineData("config.json", false)]
    [InlineData("sub-x/y.txt", false)]
    public void Only_subscription_copies_are_written(string name, bool allowed) =>
        Assert.Equal(allowed, SettingsTransfer.IsSubFileName(name));
}
