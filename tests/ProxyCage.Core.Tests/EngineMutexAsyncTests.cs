using System.Security.Cryptography;
using System.Text;

namespace ProxyCage.Core.Tests;

public class EngineMutexAsyncTests
{
    [Fact]
    public void Gate_can_be_released_after_an_async_thread_switch()
    {
        var root = Path.Combine(Path.GetTempPath(), "ceho-mutex-" + Guid.NewGuid());
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root)))[..16];
        var name = (Os.IsWindows ? @"Global\CehoProxy.Engine." : "CehoProxy.Engine.") + id;
        using var observer = new Mutex(false, name);
        var gate = EngineMutex.Acquire(root);
        var released = false;
        var thread = new Thread(() =>
        {
            gate.Dispose();
            try { released = observer.WaitOne(200); }
            catch (AbandonedMutexException) { released = true; }
            if (released) observer.ReleaseMutex();
        });
        thread.Start();
        Assert.True(thread.Join(3000));
        Assert.True(released, "The engine gate remains owned by the original thread after Dispose on another thread.");
    }
}
