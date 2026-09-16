using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public sealed class DaemonControlTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ceho-daemon-" + Guid.NewGuid().ToString("N"));

    public DaemonControlTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void Fresh_pid_for_this_process_is_reported_as_starting()
    {
        DaemonControl.MarkRunning(_root);

        Assert.True(DaemonControl.IsStarting(_root));
    }

    [Fact]
    public void Old_pid_is_not_reported_as_starting()
    {
        DaemonControl.MarkRunning(_root);
        File.SetLastWriteTimeUtc(DaemonControl.PidPath(_root), DateTime.UtcNow.AddMinutes(-2));

        Assert.False(DaemonControl.IsStarting(_root));
    }
}
