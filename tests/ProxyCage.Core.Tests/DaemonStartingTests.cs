using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class DaemonStartingTests
{
    [Fact]
    public void Recovery_mark_is_seen_until_cleared()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Assert.False(DaemonControl.IsStarting(root, TimeSpan.Zero));

            DaemonControl.MarkStarting(root, recovering: false);
            Assert.True(DaemonControl.IsStarting(root, TimeSpan.Zero));
            Assert.False(DaemonControl.IsRecovering(root));

            DaemonControl.MarkStarting(root, recovering: true);
            Assert.True(DaemonControl.IsRecovering(root));

            DaemonControl.ClearRunning(root);
            Assert.False(DaemonControl.IsStarting(root, TimeSpan.Zero));
            Assert.False(DaemonControl.IsRecovering(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Stale_mark_is_ignored()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            DaemonControl.MarkStarting(root, recovering: true);
            File.SetLastWriteTimeUtc(DaemonControl.StartingPath(root), DateTime.UtcNow.AddHours(-1));
            Assert.False(DaemonControl.IsRecovering(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
