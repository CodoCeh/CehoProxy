using ProxyCage.Core;
using Xunit;

namespace ProxyCage.Core.Tests;

public sealed class KeepEngineTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ceho-keep-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Fresh_marker_is_honoured_and_cleared()
    {
        Assert.False(DaemonControl.KeepEngineRequested(_root));

        DaemonControl.MarkKeepEngine(_root);
        Assert.True(DaemonControl.KeepEngineRequested(_root));

        DaemonControl.ClearKeepEngine(_root);
        Assert.False(DaemonControl.KeepEngineRequested(_root));
    }

    [Fact]
    public void Stale_marker_is_ignored()
    {
        DaemonControl.MarkKeepEngine(_root);
        File.SetLastWriteTimeUtc(DaemonControl.KeepEngineMarker(_root), DateTime.UtcNow.AddMinutes(-10));

        Assert.False(DaemonControl.KeepEngineRequested(_root));
    }

    [Fact]
    public async Task Running_daemon_update_keeps_the_tunnel_and_does_not_stop_it()
    {
        var stopped = false;
        var restored = false;

        var result = await TunnelShutdown.PrepareFromRunningDaemonAsync(
            new CehoConfig(), _root, Path.Combine(_root, "singbox.json"),
            () => stopped = true, () => { restored = true; return Task.CompletedTask; },
            keepEngine: true);

        Assert.True(result.Ok);
        Assert.False(stopped);
        Assert.False(restored);
        Assert.True(DaemonControl.KeepEngineRequested(_root));
    }

    [Fact]
    public async Task Running_daemon_update_without_keeping_stops_the_tunnel_first()
    {
        var stopped = false;

        await TunnelShutdown.PrepareFromRunningDaemonAsync(
            new CehoConfig(), _root, Path.Combine(_root, "singbox.json"),
            () => stopped = true, () => Task.CompletedTask);

        Assert.True(stopped);
        Assert.False(DaemonControl.KeepEngineRequested(_root));
    }
}
