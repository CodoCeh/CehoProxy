using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

[Collection("panel password")]
public class QuitReleaseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ceho-release-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly WebServer _web;
    private readonly int _port;
    private int _released;
    private int _stopped;

    public QuitReleaseTests()
    {
        Auth.ResetThrottle();
        Directory.CreateDirectory(_root);
        new CehoConfig().Save(Path.Combine(_root, "config.json"));
        _web = new WebServer(Path.Combine(_root, "config.json"), () => new WebServer.ControlState(true, null, null, null, false), _ => { })
        {
            OnRelease = () => { Interlocked.Increment(ref _released); return Task.FromResult<string?>(null); },
            OnStop = () => { Interlocked.Increment(ref _stopped); return Task.FromResult<string?>(null); },
        };
        _port = TestPanel.Start(_web);
    }

    public void Dispose()
    {
        _web.Stop();
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public async Task The_tray_quit_releases_through_the_service_not_an_ordinary_stop()
    {
        using var link = new PanelLink(_root);
        Assert.True(await link.ReleaseAsync());
        for (var i = 0; i < 50 && Volatile.Read(ref _released) == 0; i++) await Task.Delay(100);
        Assert.Equal(1, Volatile.Read(ref _released));
        Assert.Equal(0, Volatile.Read(ref _stopped));
    }

    [Fact]
    public void Release_marker_is_set_and_cleared()
    {
        Assert.False(LeakGuard.IsReleased(_root));
        LeakGuard.Release(_root);
        Assert.True(LeakGuard.IsReleased(_root));
        Assert.Null(LeakGuard.Apply(new CehoConfig { Apps = { new AppEntry { Name = "A", Folder = @"C:\A", Enabled = true } } }, _root));
        LeakGuard.ClearRelease(_root);
        Assert.False(LeakGuard.IsReleased(_root));
    }

    [Fact]
    public void Daemon_release_turns_off_the_guard_and_a_user_start_brings_it_back()
    {
        var source = Source("src/ProxyCage.Cli/Program.cs");
        var release = Between(source, "    web.OnRelease =", "    web.OnRemoveLastApp =");
        Assert.True(release.IndexOf("LeakGuard.Release(Ceho.Root)", StringComparison.Ordinal)
                    < release.IndexOf("StopTunnelLocked()", StringComparison.Ordinal));
        Assert.Contains("StopGuard();", release);
        Assert.Contains("LeakGuard.IsReleased(Ceho.Root)", Between(source, "    async Task StartGuard()", "    async Task<string?> BringEngineUp"));
        var start = Between(source, "    async Task<string?> StartTunnelLocked(", "        wanted = true;");
        Assert.Contains("LeakGuard.ClearRelease(Ceho.Root);", start);
    }

    private static string Between(string text, string first, string last)
    {
        var start = text.IndexOf(first, StringComparison.Ordinal);
        Assert.True(start >= 0, first);
        var end = text.IndexOf(last, start + first.Length, StringComparison.Ordinal);
        Assert.True(end > start, last);
        return text[start..end];
    }

    private static string Source(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ProxyCage.sln"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, relative));
    }
}
