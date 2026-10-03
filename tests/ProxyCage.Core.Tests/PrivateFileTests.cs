using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public sealed class PrivateFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ceho-write-" + Guid.NewGuid().ToString("N"));
    public PrivateFileTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Interrupted_save_leaves_complete_old_or_new_config_and_verified_copy_unchanged(int stage)
    {
        var path = Path.Combine(_root, "config.json");
        var cfg = new CehoConfig { MixedPort = 2081 };
        cfg.Save(path);
        VerifiedConfigStore.CommitVerified(_root, VerifiedConfigStore.Capture(_root, cfg, "{}"));
        var checkpointPath = Path.Combine(_root, VerifiedConfigStore.FileName);
        var checkpoint = File.ReadAllBytes(checkpointPath);
        var old = File.ReadAllBytes(path);
        cfg.MixedPort = 2082;
        Assert.Throws<IOException>(() => cfg.Save(path, (at, temporary) =>
        {
            if (at == (PrivateFile.WriteStage)stage) throw new IOException("Injected interrupted write.");
        }));
        Assert.Equal(stage == (int)PrivateFile.WriteStage.Replaced ? 2082 : 2081, CehoConfig.Load(path).MixedPort);
        if (stage != (int)PrivateFile.WriteStage.Replaced) Assert.Equal(old, File.ReadAllBytes(path));
        Assert.Equal(checkpoint, File.ReadAllBytes(checkpointPath));
        Assert.Empty(Directory.GetFiles(_root, ".ceho-private-*"));
    }

    [Fact]
    public void A_partial_first_save_never_publishes_a_truncated_configuration()
    {
        var path = Path.Combine(_root, "config.json");
        Assert.Throws<IOException>(() => new CehoConfig().Save(path, (stage, _) =>
        {
            if (stage == PrivateFile.WriteStage.PartiallyWritten) throw new IOException("Interrupted first save.");
        }));
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public void Save_does_not_claim_clean_or_chmod_foreign_crash_leftovers_or_neighbor_files()
    {
        var leftover = Path.Combine(_root, ".ceho-private-foreign-crash");
        var neighbor = Path.Combine(_root, "sub-foreign.txt");
        File.WriteAllText(leftover, "foreign unfinished file");
        File.WriteAllText(neighbor, "foreign cache");
        UnixFileMode? mode = null;
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(neighbor, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            mode = File.GetUnixFileMode(neighbor);
        }
        new CehoConfig().Save(Path.Combine(_root, "config.json"));
        Assert.Equal("foreign unfinished file", File.ReadAllText(leftover));
        Assert.Equal("foreign cache", File.ReadAllText(neighbor));
        if (!OperatingSystem.IsWindows() && mode is { } expected) Assert.Equal(expected, File.GetUnixFileMode(neighbor));
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(_root));
    }

    [Fact]
    public void Invalid_config_is_rejected_before_opening_any_file()
    {
        var directory = Path.Combine(_root, "never-created");
        Assert.Throws<InvalidDataException>(() => new CehoConfig { MixedPort = 0 }.Save(Path.Combine(directory, "config.json")));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void Bounded_read_rejects_oversized_or_invalid_utf8_content()
    {
        var path = Path.Combine(_root, "config.json");
        File.WriteAllText(path, "12345");
        Assert.Equal("12345", PrivateFile.ReadText(path, 5));
        Assert.Throws<InvalidDataException>(() => PrivateFile.ReadText(path, 4));
        File.WriteAllBytes(path, new byte[] { 0xff, 0xff });
        Assert.Throws<System.Text.DecoderFallbackException>(() => PrivateFile.ReadText(path, 5));
    }

    [Fact]
    public void Read_and_write_refuse_symbolic_links_without_touching_the_target()
    {
        if (OperatingSystem.IsWindows()) return; // Windows symlinks require extra OS privileges.
        var target = Path.Combine(_root, "foreign.json");
        File.WriteAllText(target, "foreign data");
        var link = Path.Combine(_root, "config.json");
        File.CreateSymbolicLink(link, target);
        Assert.Throws<IOException>(() => CehoConfig.Load(link));
        Assert.Throws<IOException>(() => new CehoConfig().Save(link));
        Assert.Equal("foreign data", File.ReadAllText(target));
        Assert.NotNull(new FileInfo(link).LinkTarget);
    }
}
