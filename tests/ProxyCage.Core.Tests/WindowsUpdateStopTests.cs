using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public sealed class WindowsUpdateStopTests
{
    [Fact]
    public void Does_not_release_routes_or_succeed_when_daemon_cannot_stop()
    {
        var daemonRunning = true;
        var releaseCalled = false;
        var restoreCalled = false;

        var result = TunnelShutdown.StopBeforeRelease(
            () => daemonRunning,
            () => { },
            () => false,
            () => false,
            () => false,
            () => { releaseCalled = true; return new TunnelShutdown.Result(true, null, null); },
            () => restoreCalled = true);

        Assert.False(result.Ok);
        Assert.False(releaseCalled);
        Assert.False(restoreCalled);
        Assert.True(daemonRunning);
    }

    [Fact]
    public void Restores_daemon_when_route_release_fails_after_stop()
    {
        var daemonRunning = true;
        var restoreCalled = false;

        var result = TunnelShutdown.StopBeforeRelease(
            () => daemonRunning,
            () => daemonRunning = false,
            () => true,
            () => false,
            () => false,
            () => new TunnelShutdown.Result(false, "upd_need_reboot", null),
            () => { restoreCalled = true; daemonRunning = true; });

        Assert.False(result.Ok);
        Assert.True(restoreCalled);
        Assert.True(daemonRunning);
    }

    [Fact]
    public void Releases_routes_only_after_forced_stop_is_confirmed()
    {
        var daemonRunning = true;
        var releaseCalled = false;

        var result = TunnelShutdown.StopBeforeRelease(
            () => daemonRunning,
            () => { },
            () => false,
            () => { daemonRunning = false; return true; },
            () => !daemonRunning,
            () => { releaseCalled = true; return new TunnelShutdown.Result(true, null, null); },
            () => { });

        Assert.True(result.Ok);
        Assert.True(releaseCalled);
    }
}
