namespace ProxyCage.Core;

/// <summary>
/// One controller per data directory, including launches before the PID/panel
/// pointer exists. Keep the file in place: deleting a lock file can let another
/// process lock a new inode while the old owner is still running.
/// </summary>
public sealed class ControllerLease : IDisposable
{
    private readonly FileStream _file;
    private ControllerLease(FileStream file) => _file = file;

    public static ControllerLease? TryAcquire(string root)
    {
        Directory.CreateDirectory(root);
        try
        {
            return new ControllerLease(new FileStream(Path.Combine(root, "controller.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException) { return null; }
    }

    public void Dispose() => _file.Dispose();
}
