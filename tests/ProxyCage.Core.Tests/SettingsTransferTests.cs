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

            var subs = SettingsTransfer.Import(to, file, "secret-1");
            var moved = CehoConfig.Load(Path.Combine(to, "config.json"));

            Assert.Equal(1, subs);
            Assert.Equal("telegram", Assert.Single(moved.Apps).Name);
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

    [Theory]
    [InlineData("sub-main.txt", true)]
    [InlineData("sub-../../etc/passwd.txt", false)]
    [InlineData("config.json", false)]
    [InlineData("sub-x/y.txt", false)]
    public void Only_subscription_copies_are_written(string name, bool allowed) =>
        Assert.Equal(allowed, SettingsTransfer.IsSubFileName(name));
}
