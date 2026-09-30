namespace ProxyCage.Core;

public static class UpdateRollback
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    public static string WasProtectedPath(string root) => Path.Combine(root, "update-was-protected");

    public static string MarkerPath(string root, string version) => Path.Combine(root, $"rolled-back-{version}");

    public static string BackupOf(string exe) => exe + ".old";

    public static void RememberProtection(string root, bool protectedNow)
    {
        try
        {
            if (protectedNow) File.WriteAllText(WasProtectedPath(root), DateTime.UtcNow.ToString("O"));
            else File.Delete(WasProtectedPath(root));
        }
        catch { }
    }

    public static bool WasRolledBack(string root, string version) => File.Exists(MarkerPath(root, version));

    public static bool ShouldRollBack(string root, string currentVersion, string exe, DateTime nowUtc)
    {
        try
        {
            var status = UpdateHandoff.Read(root);
            if (status is not { State: "verified" } || status.Version != currentVersion) return false;
            if (nowUtc - File.GetLastWriteTimeUtc(UpdateHandoff.PathFor(root)) > Window) return false;
            if (!File.Exists(WasProtectedPath(root))) return false;
            if (nowUtc - File.GetLastWriteTimeUtc(WasProtectedPath(root)) > Window) return false;
            return File.Exists(BackupOf(exe)) && !WasRolledBack(root, currentVersion);
        }
        catch { return false; }
    }

    public static string? PrepareCandidate(string exe)
    {
        var candidate = Path.Combine(Path.GetDirectoryName(exe) ?? ".",
            "cehoproxy-rollback" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        File.Copy(BackupOf(exe), candidate, overwrite: true);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(candidate,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return candidate;
    }

    public static void Mark(string root, string version)
    {
        try { File.WriteAllText(MarkerPath(root, version), DateTime.UtcNow.ToString("O")); } catch { }
        try { File.Delete(WasProtectedPath(root)); } catch { }
    }
}
