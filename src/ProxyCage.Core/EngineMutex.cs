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
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ManualResetEventSlim _release = new(false);
    private Thread? _owner;
    private Exception? _error;
    private int _disposed;

    private EngineMutex(Mutex mutex) => _mutex = mutex;

    public static EngineMutex Acquire(string root)
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root ?? "")))[..16];
        var name = (Os.IsWindows ? @"Global\CehoProxy.Engine." : "CehoProxy.Engine.") + id;
        var mutex = new Mutex(false, name);
        var gate = new EngineMutex(mutex);
        // Mutex привязан к потоку: продолжение после await не может его освободить.
        gate._owner = new Thread(gate.Own) { IsBackground = true, Name = "CehoProxy engine gate" };
        gate._owner.Start();
        gate._ready.Wait();
        if (gate._error is not null)
        {
            var error = gate._error;
            gate.Dispose();
            throw error;
        }
        return gate;
    }

    private void Own()
    {
        var held = false;
        try
        {
            try { held = _mutex.WaitOne(TimeSpan.FromMinutes(2)); }
            catch (AbandonedMutexException) { held = true; }
            if (!held) throw new TimeoutException("Не дождались очереди движка за 2 минуты.");
        }
        catch (Exception ex) { _error = ex; }
        finally { _ready.Set(); }
        if (!held) return;
        try { _release.Wait(); }
        finally { _mutex.ReleaseMutex(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _release.Set();
        _owner?.Join();
        _mutex.Dispose();
        _ready.Dispose();
        _release.Dispose();
    }
}
