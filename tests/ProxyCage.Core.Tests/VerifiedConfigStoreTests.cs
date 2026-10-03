using System.Text.Json.Nodes;
using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public sealed class VerifiedConfigStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ceho-verified-test-" + Guid.NewGuid().ToString("N"));
    private string ConfigPath => Path.Combine(_root, "config.json");
    private string RuntimePath => Path.Combine(_root, "singbox.json");
    private const string Rules = "{\"outbounds\":[{\"type\":\"direct\",\"tag\":\"direct\"}]}";
    public VerifiedConfigStoreTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void Saving_or_capturing_never_marks_settings_verified()
    {
        var cfg = new CehoConfig();
        cfg.Save(ConfigPath);
        _ = VerifiedConfigStore.Capture(_root, cfg, Rules);
        Assert.False(VerifiedConfigStore.ReadStatus(_root).Available);
    }

    [Fact]
    public void Commit_is_immutable_and_excludes_local_password_and_update_settings()
    {
        var cfg = new CehoConfig { PasswordHash = "private-password-hash", PasswordSalt = "private-salt", UpdateRepo = "private/repo" };
        cfg.Apps.Add(new AppEntry { Name = "app-one", Folder = "/fake/app" });
        var candidate = VerifiedConfigStore.Capture(_root, cfg, Rules);
        cfg.Apps.Clear();
        VerifiedConfigStore.CommitVerified(_root, candidate);
        var content = File.ReadAllText(Path.Combine(_root, VerifiedConfigStore.FileName));
        Assert.DoesNotContain("private-password-hash", content);
        Assert.DoesNotContain("private-salt", content);
        Assert.DoesNotContain("private/repo", content);
        Assert.Equal(1, VerifiedConfigStore.ReadStatus(_root).Apps);
    }

    [Fact]
    public void Restore_recovers_routing_and_cache_but_preserves_current_panel_credentials_and_preferences()
    {
        var cfg = new CehoConfig { Language = "ru", MixedPort = 2081, PasswordHash = "old-secret", WebPort = 8800 };
        cfg.Apps.Add(new AppEntry { Name = "original", Folder = "/fake/original" });
        cfg.Subscriptions.Add(new SubscriptionEntry { Name = "test", Url = "https://example.test/subscription" });
        cfg.Save(ConfigPath);
        File.WriteAllText(Path.Combine(_root, "sub-test.txt"), "synthetic cached node");
        VerifiedConfigStore.CommitVerified(_root, VerifiedConfigStore.Capture(_root, cfg, Rules));
        var checkpoint = VerifiedConfigStore.ReadStatus(_root);
        cfg.Language = "en";
        cfg.PanelMode = CehoConfig.PanelModeSimple;
        cfg.PasswordHash = "new-secret";
        cfg.PasswordSalt = "new-salt";
        cfg.UpdateRepo = "new/repo";
        cfg.AutoUpdate = true;
        cfg.WebPort = 8811;
        cfg.MixedPort = 2090;
        cfg.Apps.Clear();
        cfg.Save(ConfigPath);
        File.WriteAllText(RuntimePath, "{}");
        File.WriteAllText(Path.Combine(_root, "sub-test.txt"), "new broken cache");

        VerifiedConfigStore.Restore(_root, expectedVerifiedUtc: checkpoint.VerifiedUtc);
        var restored = CehoConfig.Load(ConfigPath);
        Assert.Equal("original", Assert.Single(restored.Apps).Name);
        Assert.Equal(2081, restored.MixedPort);
        Assert.Equal("new-secret", restored.PasswordHash);
        Assert.Equal("new-salt", restored.PasswordSalt);
        Assert.Equal("en", restored.Language);
        Assert.Equal(CehoConfig.PanelModeSimple, restored.PanelMode);
        Assert.Equal("new/repo", restored.UpdateRepo);
        Assert.True(restored.AutoUpdate);
        Assert.Equal(8811, restored.WebPort);
        Assert.Equal(Rules, File.ReadAllText(RuntimePath));
        Assert.True(File.GetLastWriteTimeUtc(RuntimePath) >= File.GetLastWriteTimeUtc(ConfigPath));
        Assert.Equal("synthetic cached node", File.ReadAllText(Path.Combine(_root, "sub-test.txt")));
        Assert.Equal(checkpoint.VerifiedUtc, VerifiedConfigStore.ReadStatus(_root).VerifiedUtc);
    }

    [Fact]
    public void Invalid_or_failed_candidate_keeps_last_verified_checkpoint()
    {
        var cfg = new CehoConfig();
        cfg.Save(ConfigPath);
        VerifiedConfigStore.CommitVerified(_root, VerifiedConfigStore.Capture(_root, cfg, Rules));
        var before = File.ReadAllText(Path.Combine(_root, VerifiedConfigStore.FileName));
        Assert.ThrowsAny<Exception>(() => VerifiedConfigStore.Capture(_root, cfg, "broken rules"));
        _ = VerifiedConfigStore.Capture(_root, cfg, "{}"); // Simulated failed apply: no commit.
        Assert.Equal(before, File.ReadAllText(Path.Combine(_root, VerifiedConfigStore.FileName)));
    }

    [Fact]
    public void Checkpoint_change_requires_a_fresh_review()
    {
        var cfg = new CehoConfig();
        cfg.Save(ConfigPath);
        VerifiedConfigStore.CommitVerified(_root, VerifiedConfigStore.Capture(_root, cfg, Rules));
        var expected = VerifiedConfigStore.ReadStatus(_root).VerifiedUtc!.Value.AddSeconds(-1);
        Assert.Throws<InvalidOperationException>(() => VerifiedConfigStore.Restore(_root, expectedVerifiedUtc: expected));
    }

    [Theory]
    [InlineData(false, "{}")]
    [InlineData(true, "{\"outbounds\":[{\"type\":\"naive\",\"server\":\"example.test\",\"server_port\":443,\"tls\":{\"insecure\":true}}]}")]
    public void Weaker_security_requires_explicit_acknowledgement(bool failClosed, string targetRules)
    {
        var target = new CehoConfig { FailClosed = failClosed };
        VerifiedConfigStore.CommitVerified(_root, VerifiedConfigStore.Capture(_root, target, targetRules));
        new CehoConfig { FailClosed = true }.Save(ConfigPath);
        File.WriteAllText(RuntimePath, Rules);
        Assert.True(VerifiedConfigStore.ReadStatus(_root).RequiresSecurityAcknowledgement);
        Assert.Throws<InvalidOperationException>(() => VerifiedConfigStore.Restore(_root));
        VerifiedConfigStore.Restore(_root, acknowledgeSecurityChange: true);
        Assert.Equal(targetRules, File.ReadAllText(RuntimePath));
    }

    [Fact]
    public void Unsafe_cache_path_in_checkpoint_is_rejected_before_any_change()
    {
        var cfg = new CehoConfig();
        cfg.Save(ConfigPath);
        VerifiedConfigStore.CommitVerified(_root, VerifiedConfigStore.Capture(_root, cfg, Rules));
        var path = Path.Combine(_root, VerifiedConfigStore.FileName);
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        json["Caches"]!["sub-../../outside.txt"] = "unsafe";
        File.WriteAllText(path, json.ToJsonString());
        Assert.False(VerifiedConfigStore.ReadStatus(_root).Available);
        Assert.Throws<InvalidDataException>(() => VerifiedConfigStore.Restore(_root));
    }

    [Fact]
    public void Damaged_rules_are_rejected_and_no_files_are_replaced()
    {
        var cfg = new CehoConfig();
        cfg.Save(ConfigPath);
        VerifiedConfigStore.CommitVerified(_root, VerifiedConfigStore.Capture(_root, cfg, Rules));
        var path = Path.Combine(_root, VerifiedConfigStore.FileName);
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        json["Runtime"] = "{}";
        File.WriteAllText(path, json.ToJsonString());
        var before = File.ReadAllText(ConfigPath);
        Assert.Throws<InvalidDataException>(() => VerifiedConfigStore.Restore(_root));
        Assert.Equal(before, File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void A_failed_restore_unwinds_files_already_replaced()
    {
        var cfg = new CehoConfig();
        cfg.Subscriptions.Add(new SubscriptionEntry { Name = "test", Url = "https://example.test/sub" });
        cfg.Save(ConfigPath);
        var cache = Path.Combine(_root, "sub-test.txt");
        File.WriteAllText(cache, "verified-cache");
        VerifiedConfigStore.CommitVerified(_root, VerifiedConfigStore.Capture(_root, cfg, Rules));
        File.WriteAllText(cache, "current-cache");
        // A directory at a target path simulates a failing atomic replacement without OS changes.
        Directory.CreateDirectory(RuntimePath);
        var before = File.ReadAllText(ConfigPath);
        Assert.Throws<IOException>(() => VerifiedConfigStore.Restore(_root));
        Assert.Equal("current-cache", File.ReadAllText(cache));
        Assert.Equal(before, File.ReadAllText(ConfigPath));
        Assert.True(VerifiedConfigStore.ReadStatus(_root).Available);
    }

    [Fact]
    public void Local_secret_files_are_private_and_writes_leave_no_temporary_files()
    {
        var cfg = new CehoConfig();
        cfg.Save(ConfigPath);
        VerifiedConfigStore.CommitVerified(_root, VerifiedConfigStore.Capture(_root, cfg, Rules));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(ConfigPath));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(Path.Combine(_root, VerifiedConfigStore.FileName)));
        }
        Assert.Empty(Directory.GetFiles(_root, ".ceho-private-*"));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void Injected_restore_failures_before_or_after_each_rename_restore_all_previous_files(int failingWrite, bool afterRename)
    {
        var cfg = new CehoConfig { MixedPort = 2081,
            Subscriptions = { new SubscriptionEntry { Name = "test", Url = "https://example.test/sub" } } };
        var cache = Path.Combine(_root, "sub-test.txt");
        cfg.Save(ConfigPath);
        File.WriteAllText(cache, "verified-cache");
        VerifiedConfigStore.CommitVerified(_root, VerifiedConfigStore.Capture(_root, cfg, Rules));
        cfg.MixedPort = 2082;
        cfg.Save(ConfigPath);
        File.WriteAllText(cache, "current-cache");
        File.WriteAllText(RuntimePath, "{}");
        var paths = new[] { cache, RuntimePath, ConfigPath, Path.Combine(_root, VerifiedConfigStore.FileName) };
        var before = paths.ToDictionary(p => p, p => (Bytes: File.ReadAllBytes(p), Time: File.GetLastWriteTimeUtc(p)));
        var count = 0;
        var error = Assert.Throws<IOException>(() => VerifiedConfigStore.Restore(_root, false, null, (path, text) =>
        {
            var current = count++;
            PrivateFile.Write(path, text, (stage, _) =>
            {
                if (current == failingWrite && stage == (afterRename ? PrivateFile.WriteStage.Replaced : PrivateFile.WriteStage.PartiallyWritten))
                    throw new IOException("Injected restore interruption.");
            });
        }));
        Assert.Contains("previous settings were retained", error.Message);
        foreach (var path in paths)
        {
            Assert.Equal(before[path].Bytes, File.ReadAllBytes(path));
            Assert.Equal(before[path].Time, File.GetLastWriteTimeUtc(path));
        }
        Assert.True(VerifiedConfigStore.ReadStatus(_root).Available);
        Assert.Empty(Directory.GetFiles(_root, ".ceho-private-*"));
    }

    [Fact]
    public void Interrupted_checkpoint_commit_keeps_the_previous_verified_generation()
    {
        var cfg = new CehoConfig();
        VerifiedConfigStore.CommitVerified(_root, VerifiedConfigStore.Capture(_root, cfg, Rules));
        var path = Path.Combine(_root, VerifiedConfigStore.FileName);
        var before = File.ReadAllBytes(path);
        cfg.MixedPort++;
        var candidate = VerifiedConfigStore.Capture(_root, cfg, Rules);
        Assert.Throws<IOException>(() => VerifiedConfigStore.CommitVerified(_root, candidate, (target, text) =>
            PrivateFile.Write(target, text, (stage, _) =>
            {
                if (stage == PrivateFile.WriteStage.PartiallyWritten) throw new IOException("Interrupted checkpoint save.");
            })));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.True(VerifiedConfigStore.ReadStatus(_root).Available);
    }

    [Fact]
    public void Corrupt_cache_bytes_are_rejected_before_any_restore_write()
    {
        var cfg = new CehoConfig { Subscriptions = { new SubscriptionEntry { Name = "test", Url = "https://example.test/sub" } } };
        cfg.Save(ConfigPath);
        File.WriteAllText(Path.Combine(_root, "sub-test.txt"), "verified cache");
        VerifiedConfigStore.CommitVerified(_root, VerifiedConfigStore.Capture(_root, cfg, Rules));
        var checkpoint = Path.Combine(_root, VerifiedConfigStore.FileName);
        var json = JsonNode.Parse(File.ReadAllText(checkpoint))!;
        json["Caches"]!["sub-test.txt"] = "damaged cache";
        File.WriteAllText(checkpoint, json.ToJsonString());
        var before = File.ReadAllBytes(ConfigPath);
        Assert.False(VerifiedConfigStore.ReadStatus(_root).Available);
        Assert.Throws<InvalidDataException>(() => VerifiedConfigStore.Restore(_root, true, null, (_, _) => throw new Xunit.Sdk.XunitException("Must not write.")));
        Assert.Equal(before, File.ReadAllBytes(ConfigPath));
    }

    [Fact]
    public void Stale_review_and_weaker_ipv6_security_are_rejected_before_any_restore_write()
    {
        var cfg = new CehoConfig { TunIpv6 = false };
        VerifiedConfigStore.CommitVerified(_root, VerifiedConfigStore.Capture(_root, cfg, Rules));
        var expected = VerifiedConfigStore.ReadStatus(_root).VerifiedUtc;
        VerifiedConfigStore.CommitVerified(_root, VerifiedConfigStore.Capture(_root, cfg, Rules));
        new CehoConfig { TunIpv6 = true }.Save(ConfigPath);
        Assert.True(VerifiedConfigStore.ReadStatus(_root).RequiresSecurityAcknowledgement);
        Assert.Throws<InvalidOperationException>(() => VerifiedConfigStore.Restore(_root, true, expected, (_, _) => throw new Xunit.Sdk.XunitException("Must not write.")));
        Assert.Throws<InvalidOperationException>(() => VerifiedConfigStore.Restore(_root, false, null, (_, _) => throw new Xunit.Sdk.XunitException("Must not write.")));
        VerifiedConfigStore.Restore(_root, true, VerifiedConfigStore.ReadStatus(_root).VerifiedUtc);
        Assert.False(CehoConfig.Load(ConfigPath).TunIpv6);
    }

    [Fact]
    public void Invalid_candidate_cannot_replace_a_good_snapshot()
    {
        var cfg = new CehoConfig();
        VerifiedConfigStore.CommitVerified(_root, VerifiedConfigStore.Capture(_root, cfg, Rules));
        var path = Path.Combine(_root, VerifiedConfigStore.FileName);
        var before = File.ReadAllBytes(path);
        cfg.MixedPort = -1;
        Assert.Throws<InvalidDataException>(() => VerifiedConfigStore.Capture(_root, cfg, Rules));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Valid_hashes_do_not_authorize_unrelated_cache_paths_or_invalid_settings()
    {
        var cfg = new CehoConfig();
        cfg.Save(ConfigPath);
        VerifiedConfigStore.CommitVerified(_root, VerifiedConfigStore.Capture(_root, cfg, Rules));
        var path = Path.Combine(_root, VerifiedConfigStore.FileName);
        var original = File.ReadAllText(path);
        var json = JsonNode.Parse(original)!;
        json["Caches"]!["sub-foreign.txt"] = "unrelated data";
        json["CacheHashes"]!["sub-foreign.txt"] = Hash("unrelated data");
        File.WriteAllText(path, json.ToJsonString());
        Assert.Throws<InvalidDataException>(() => VerifiedConfigStore.Restore(_root, true, null, (_, _) => throw new Xunit.Sdk.XunitException("Must not write.")));

        json = JsonNode.Parse(original)!;
        var stored = JsonNode.Parse(json["Config"]!.GetValue<string>())!;
        stored["MixedPort"] = 0;
        var text = stored.ToJsonString();
        json["Config"] = text;
        json["ConfigHash"] = Hash(text);
        File.WriteAllText(path, json.ToJsonString());
        Assert.Throws<InvalidDataException>(() => VerifiedConfigStore.Restore(_root, true, null, (_, _) => throw new Xunit.Sdk.XunitException("Must not write.")));
        Assert.Equal(cfg.MixedPort, CehoConfig.Load(ConfigPath).MixedPort);
    }

    [Fact]
    public void Linked_cache_sources_and_restore_targets_are_not_followed_or_replaced()
    {
        if (OperatingSystem.IsWindows()) return;
        var cfg = new CehoConfig { Subscriptions = { new SubscriptionEntry { Name = "test", Url = "https://example.test/sub" } } };
        cfg.Save(ConfigPath);
        var foreign = Path.Combine(_root, "foreign.txt");
        var cache = Path.Combine(_root, "sub-test.txt");
        File.WriteAllText(foreign, "foreign data");
        File.CreateSymbolicLink(cache, foreign);
        Assert.Throws<IOException>(() => VerifiedConfigStore.Capture(_root, cfg, Rules));
        File.Delete(cache);
        File.WriteAllText(cache, "verified data");
        VerifiedConfigStore.CommitVerified(_root, VerifiedConfigStore.Capture(_root, cfg, Rules));
        File.Delete(cache);
        File.CreateSymbolicLink(cache, foreign);
        Assert.Throws<IOException>(() => VerifiedConfigStore.Restore(_root, true, null, (_, _) => throw new Xunit.Sdk.XunitException("Must not write.")));
        Assert.Equal("foreign data", File.ReadAllText(foreign));
        Assert.NotNull(new FileInfo(cache).LinkTarget);
    }

    private static string Hash(string text) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
}
