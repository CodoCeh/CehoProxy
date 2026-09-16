using System.Security.Cryptography;
using System.Text;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ProxyCage.Core;

public static class Auth
{
    private const int Iterations = 210_000;
    private const int SaltBytes = 16;
    private const int KeyBytes = 32;

    public static bool HasPassword(CehoConfig cfg) =>
        !string.IsNullOrEmpty(cfg.PasswordHash) && !string.IsNullOrEmpty(cfg.PasswordSalt);

    public static void SetPassword(CehoConfig cfg, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        cfg.PasswordSalt = Convert.ToBase64String(salt);
        cfg.PasswordHash = Convert.ToBase64String(Derive(password, salt));
    }

    public static void ClearPassword(CehoConfig cfg)
    {
        cfg.PasswordHash = null;
        cfg.PasswordSalt = null;
    }

    public static bool Verify(CehoConfig cfg, string? password)
    {
        if (!HasPassword(cfg)) return true;
        if (string.IsNullOrEmpty(password)) return false;

        try
        {
            var salt = Convert.FromBase64String(cfg.PasswordSalt!);
            var expected = Convert.FromBase64String(cfg.PasswordHash!);

            return CryptographicOperations.FixedTimeEquals(Derive(password, salt), expected);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Derive(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, KeyBytes);

    private static readonly Dictionary<string, DateTime> Sessions = new(StringComparer.Ordinal);
    private static readonly object Gate = new();
    private static readonly TimeSpan SessionLife = TimeSpan.FromHours(12);

    public static string IssueSession()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        lock (Gate)
        {
            Sweep();
            Sessions[token] = DateTime.UtcNow + SessionLife;
        }
        return token;
    }

    public static bool ValidSession(string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        lock (Gate)
        {
            Sweep();
            return Sessions.ContainsKey(token);
        }
    }

    public static void DropAllSessions()
    {
        lock (Gate) Sessions.Clear();
    }

    private static void Sweep()
    {
        var now = DateTime.UtcNow;
        foreach (var dead in Sessions.Where(s => s.Value < now).Select(s => s.Key).ToList())
            Sessions.Remove(dead);
    }

    public static void RestrictConfigAccess(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (OperatingSystem.IsWindows())
            {
                RestrictWindowsAccess(path, dir);
                return;
            }
            if (!string.IsNullOrEmpty(dir)) File.SetUnixFileMode(dir,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            if (File.Exists(path)) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "singbox.json")))
                File.SetUnixFileMode(Path.Combine(dir, "singbox.json"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
            if (!string.IsNullOrEmpty(dir))
                foreach (var cache in Directory.GetFiles(dir, "sub-*.txt"))
                    File.SetUnixFileMode(cache, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch { }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void RestrictWindowsAccess(string path, string? dir)
    {
        if (string.IsNullOrEmpty(dir)) return;
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var directoryAcl = new DirectorySecurity();
        directoryAcl.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { admins, system })
            directoryAcl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                inheritance, PropagationFlags.None, AccessControlType.Allow));
        directoryAcl.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.ReadAndExecute,
            inheritance, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(dir).SetAccessControl(directoryAcl);
        var secrets = Directory.GetFiles(dir, "sub-*.txt").Concat(new[] { path, Path.Combine(dir, "singbox.json") });
        foreach (var secret in secrets.Where(File.Exists))
        {
            var acl = new FileSecurity();
            acl.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { admins, system })
                acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(secret).SetAccessControl(acl);
        }
    }

    public static void WritePanelPointer(string root, int panelPort, int proxyPort)
    {
        try
        {
            Directory.CreateDirectory(root);
            var file = Path.Combine(root, "panel.port");
            File.WriteAllText(file, $"{panelPort} {proxyPort}");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }
        catch { }
    }

    public static int? ReadPanelPointer(string root) => ReadPointer(root, 0);

    public static int? ReadProxyPointer(string root) => ReadPointer(root, 1);

    private static int? ReadPointer(string root, int index)
    {
        try
        {
            var file = Path.Combine(root, "panel.port");
            if (!File.Exists(file)) return null;
            var parts = File.ReadAllText(file).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > index && int.TryParse(parts[index], out var p) ? p : null;
        }
        catch
        {
            return null;
        }
    }
}
