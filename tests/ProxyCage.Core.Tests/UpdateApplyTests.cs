using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public sealed class UpdateApplyTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ceho-apply-" + Guid.NewGuid().ToString("N")[..8]);

    public UpdateApplyTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string Fake(string name, string version)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, $"#!/bin/sh\necho {version}\necho https://example.invalid\n");
        File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return path;
    }

    [Fact]
    public void Version_is_read_from_the_first_line()
    {
        if (OperatingSystem.IsWindows()) return;

        Assert.Equal("1.2.73", Updater.ReadVersion(Fake("cehoproxy", "1.2.73")));
    }

    [Fact]
    public void A_confirmed_swap_leaves_the_new_version_and_one_rollback()
    {
        if (OperatingSystem.IsWindows()) return;

        var target = Fake("cehoproxy", "1.2.72");
        var staged = Fake("cehoproxy.new", "1.2.73");

        var applied = Updater.ApplyDownloaded(staged, target, "1.2.73");

        Assert.True(applied.Ok);
        Assert.Equal("1.2.73", applied.Installed);
        Assert.Equal("1.2.73", Updater.ReadVersion(target));
        Assert.False(File.Exists(staged));
        Assert.Equal("1.2.72", Updater.ReadVersion(target + ".old"));
    }

    [Fact]
    public void A_swap_that_did_not_take_is_rolled_back_and_reported()
    {
        if (OperatingSystem.IsWindows()) return;

        var target = Fake("cehoproxy", "1.2.72");
        var staged = Fake("cehoproxy.new", "1.2.72");

        var applied = Updater.ApplyDownloaded(staged, target, "1.2.73", retryDelay: TimeSpan.Zero);

        Assert.False(applied.Ok);
        Assert.Equal("1.2.73", applied.Expected);
        Assert.Equal("1.2.72", applied.Installed);
        Assert.Equal("1.2.72", Updater.ReadVersion(target));
    }

    [Fact]
    public void A_new_version_that_fails_its_first_start_is_asked_again()
    {
        if (OperatingSystem.IsWindows()) return;

        var target = Fake("cehoproxy", "1.2.72");
        var staged = Fake("cehoproxy.new", "1.2.73");
        var seen = Path.Combine(_root, "seen");
        File.WriteAllText(staged,
            $"#!/bin/sh\nif [ ! -f '{seen}' ]; then touch '{seen}'; kill -9 $$; fi\necho 1.2.73\n");
        var notes = new List<string>();

        var applied = Updater.ApplyDownloaded(staged, target, "1.2.73", notes.Add, TimeSpan.Zero);

        Assert.True(applied.Ok);
        Assert.Equal("1.2.73", Updater.ReadVersion(target));
        Assert.Single(notes);
    }

    [Fact]
    public void Old_copies_go_away_and_one_rollback_stays()
    {
        var binary = Path.Combine(_root, "cehoproxy");
        foreach (var name in new[]
                 {
                     "cehoproxy", "cehoproxy.old", "cehoproxy.new", "cehoproxy.1.2.55.bak",
                     "cehoproxy.before-20260901", "cehoproxy.1.2.60.old",
                     "cehoproxy.pid", "cehoproxy.log", "ceho-engine.old",
                 })
            File.WriteAllText(Path.Combine(_root, name), "x");

        var removed = Installer.SweepOldBinaries(_root, binary);

        Assert.Equal(4, removed);
        Assert.True(File.Exists(binary));
        Assert.True(File.Exists(binary + ".old"));
        Assert.True(File.Exists(Path.Combine(_root, "cehoproxy.pid")));
        Assert.True(File.Exists(Path.Combine(_root, "cehoproxy.log")));
        Assert.True(File.Exists(Path.Combine(_root, "ceho-engine.old")));
        Assert.False(File.Exists(Path.Combine(_root, "cehoproxy.new")));
        Assert.False(File.Exists(Path.Combine(_root, "cehoproxy.1.2.55.bak")));
        Assert.False(File.Exists(Path.Combine(_root, "cehoproxy.before-20260901")));
        Assert.False(File.Exists(Path.Combine(_root, "cehoproxy.1.2.60.old")));
    }

    [Fact]
    public void Sweeping_an_empty_folder_is_not_an_error()
    {
        Assert.Equal(0, Installer.SweepOldBinaries(_root, Path.Combine(_root, "cehoproxy")));
    }

    [Fact]
    public void Protection_comes_back_even_when_the_swap_did_not_take()
    {
        var asked = 0;
        var shutdown = new TunnelShutdown.Result(true, null, null) { DaemonWasRunning = true };

        Assert.True(Updater.RestoreProtectionAfterApply(shutdown, () => { asked++; return true; }));
        Assert.Equal(1, asked);
    }

    [Fact]
    public void Protection_comes_back_when_only_autostart_was_on()
    {
        var shutdown = new TunnelShutdown.Result(true, null, null) { AutostartWasOn = true };

        Assert.False(Updater.RestoreProtectionAfterApply(shutdown, () => false));
    }

    [Fact]
    public void Nothing_is_restarted_when_protection_was_already_off()
    {
        var shutdown = new TunnelShutdown.Result(true, null, null);

        Assert.True(Updater.RestoreProtectionAfterApply(
            shutdown, () => throw new InvalidOperationException("не должны перезапускать")));
    }
}
