namespace ProxyCage.Core.Tests;

public class ControllerLeaseTests
{
    [Fact]
    public void Concurrent_controller_launch_is_refused_and_release_allows_reuse()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-controller-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            using (var first = ControllerLease.TryAcquire(root))
            {
                Assert.NotNull(first);
                using var second = ControllerLease.TryAcquire(root);
                Assert.Null(second);
            }
            using var next = ControllerLease.TryAcquire(root);
            Assert.NotNull(next);
            Assert.True(File.Exists(Path.Combine(root, "controller.lock")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Distinct_profile_directories_do_not_share_a_controller_lease()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-controller-" + Guid.NewGuid());
        try
        {
            using var first = ControllerLease.TryAcquire(Path.Combine(root, "one"));
            using var second = ControllerLease.TryAcquire(Path.Combine(root, "two"));
            Assert.NotNull(first); Assert.NotNull(second);
        }
        finally { Directory.Delete(root, true); }
    }
}
