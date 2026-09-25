namespace ProxyCage.Core.Tests;

public class FullUninstallCleanupTests
{
    [Fact]
    public void Removes_product_caches_but_preserves_unrelated_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var name in new[]
                     {
                         "singbox.json", "hwid.txt", "node-country-cache.json",
                         "node-country-cache.json.tmp", "tun-devices.txt",
                         "cehoproxy.log", "cehoproxy.log.1", "sing-box.log.1",
                         "cehoproxy-update.log", "cehoproxy-update.log.1",
                         "update-status.json", "update-status.json.tmp",
                         "crash-old.log", "cehoproxy.exe.old", "cehoproxy.exe.new",
                         "cehoproxy.old", "cehoproxy.new", "sing-box.old", "sing-box.new",
                         "ceho-engine.old", "ceho-engine.new",
                         "cehoproxy.before-1.2.69", "config.before-1.2.69.json",
                         "ceho-engine.exe.old", "sing-box.exe.new", "libcronet.dll.dl",
                         "libcronet.so", "libcronet.so.old", "libcronet.so.new", "libcronet.so.dl",
                         "dbip-country-lite.mmdb", "dbip-country-lite.mmdb.tmp",
                         "dbip-country-lite.mmdb.gz.tmp", "dbip-country-lite.mmdb.bundled.tmp",
                         "sing-box-1.14.0-windows-amd64.zip", "sing-box-1.14.0-linux-amd64.tar.gz",
                         "update-relaunch.ps1", "pick-app.ps1", "pick-app-launch.vbs",
                         "pick-app-result.txt", "user-alias.path", ".write-probe"
                     })
                File.WriteAllText(Path.Combine(root, name), "test");

            foreach (var name in new[] { "geo-probes", "geoip", "engine-tmp" })
            {
                var dir = Path.Combine(root, name);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "cache"), "test");
            }

            File.WriteAllText(Path.Combine(root, "user-file.txt"), "keep");
            File.WriteAllText(Path.Combine(root, "custom.old"), "keep");
            File.WriteAllText(Path.Combine(root, "custom.new"), "keep");
            File.WriteAllText(Path.Combine(root, "custom.log.1"), "keep");
            File.WriteAllText(Path.Combine(root, "dbip-country-lite.mmdb.backup"), "keep");
            var rulesets = Path.Combine(root, "rulesets");
            Directory.CreateDirectory(rulesets);
            File.WriteAllText(Path.Combine(rulesets, "geoip-user.srs"), "keep");
            Installer.RemoveRuntimeFiles(root);

            Assert.Equal(new[] { "custom.log.1", "custom.new", "custom.old", "dbip-country-lite.mmdb.backup", "user-file.txt" },
                Directory.GetFileSystemEntries(root).Where(p => !Path.GetFileName(p).Equals("rulesets", StringComparison.Ordinal))
                    .Select(Path.GetFileName).OrderBy(n => n));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(rulesets, "geoip-user.srs")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Removes_only_symbolic_aliases_targeting_install_root()
    {
        if (OperatingSystem.IsWindows()) return;

        var root = Path.Combine(Path.GetTempPath(), "ceho-alias-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var aliasDir = Path.Combine(root, "aliases");
        Directory.CreateDirectory(aliasDir);
        try
        {
            var binary = Installer.BinaryPath(root);
            File.WriteAllText(binary, "ours");
            var ownedAlias = Path.Combine(aliasDir, "chp");
            File.CreateSymbolicLink(ownedAlias, binary);

            var externalFile = Path.Combine(root, "other-tool");
            File.WriteAllText(externalFile, "keep");
            var externalAlias = Path.Combine(aliasDir, "cehoproxy");
            File.CreateSymbolicLink(externalAlias, externalFile);
            var regularFile = Path.Combine(aliasDir, "chp-custom");
            File.WriteAllText(regularFile, "keep");

            Installer.RemoveOwnedAlias(ownedAlias, root);
            Installer.RemoveOwnedAlias(externalAlias, root);
            Installer.RemoveOwnedAlias(regularFile, root);

            Assert.False(File.Exists(ownedAlias));
            Assert.True(File.Exists(externalAlias));
            Assert.True(File.Exists(regularFile));
            Assert.True(File.Exists(externalFile));

            var userBin = Path.Combine(root, "home", "bin");
            Directory.CreateDirectory(userBin);
            var systemAlias = Path.Combine(aliasDir, "system-chp");
            File.CreateSymbolicLink(systemAlias, binary);
            var userAlias = Path.Combine(userBin, "chp");
            File.CreateSymbolicLink(userAlias, systemAlias);
            Installer.RecordUserAlias(systemAlias, userAlias);
            var recordedUserAlias = File.ReadAllText(Path.Combine(root, "user-alias.path"));
            Installer.RemoveUserAlias(recordedUserAlias, systemAlias, root);
            Assert.False(File.Exists(userAlias));

            var customAlias = Path.Combine(userBin, "other");
            File.CreateSymbolicLink(customAlias, externalFile);
            Installer.RemoveUserAlias(customAlias, systemAlias, root);
            Assert.True(File.Exists(customAlias));

            var otherSystemAlias = Path.Combine(aliasDir, "other-system-chp");
            File.CreateSymbolicLink(otherSystemAlias, externalFile);
            var otherUserAlias = Path.Combine(userBin, "other-chp");
            File.CreateSymbolicLink(otherUserAlias, otherSystemAlias);
            Installer.RemoveUserAlias(otherUserAlias, otherSystemAlias, root);
            Assert.True(File.Exists(otherUserAlias));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
