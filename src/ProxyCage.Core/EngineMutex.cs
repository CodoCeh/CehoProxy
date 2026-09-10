using System.Security.Cryptography;
using System.Text;

namespace ProxyCage.Core;

/// <summary>
/// Одна очередь на подъём/остановку движка и уборку Wintun: и демон, и
/// «chp doctor fix», и кнопка в панели. SemaphoreSlim виден только своему
/// процессу, поэтому очередь — именованный Mutex (на Windows — Global\).
/// </summary>
public sealed class EngineMutex : IDisposable
{
    private readonly Mutex _mutex;
    private bool _held;

    private EngineMutex(Mutex mutex) => _mutex = mutex;

    public static EngineMutex Acquire(string root)
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root ?? "")))[..16];
        var name = (Os.IsWindows ? @"Global\CehoProxy.Engine." : "CehoProxy.Engine.") + id;
        var mutex = new Mutex(false, name);
        var gate = new EngineMutex(mutex);
        try
        {
            gate._held = mutex.WaitOne(TimeSpan.FromMinutes(2));
            if (!gate._held)
                Log.Warn("не дождались очереди движка за 2 минуты, продолжаю без неё");
        }
        catch (AbandonedMutexException)
        {
            gate._held = true;
        }
        return gate;
    }

    public void Dispose()
    {
        if (_held)
        {
            try { _mutex.ReleaseMutex(); } catch { }
        }
        _mutex.Dispose();
    }
}
