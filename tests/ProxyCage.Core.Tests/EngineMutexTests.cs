using ProxyCage.Core;

namespace ProxyCage.Core.Tests;

public class EngineMutexTests
{
    [Fact]
    public async Task Second_holder_waits_until_the_first_releases()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var first = EngineMutex.Acquire(root);
        var acquired = false;
        var second = Task.Run(() => { using var gate = EngineMutex.Acquire(root); acquired = true; });
        await Task.Delay(400);
        Assert.False(acquired);
        first.Dispose();
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(acquired);
    }

    [Fact]
    public void A_missing_folder_is_created_for_the_lock()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-missing-" + Guid.NewGuid().ToString("N"));
        using var gate = EngineMutex.Acquire(root);
        Assert.Equal(Os.IsWindows, !File.Exists(Path.Combine(root, "engine.lock")));
    }
}
