using System.Security.Cryptography;
using System.Text;

namespace ProxyCage.Core;

/// <summary>
/// Identity of a selected local application, separate from the process-rule folder.
/// Names, process basenames and routing coverage are never application identities.
/// </summary>
public static class AppIdentity
{
    public static string PathOf(AppEntry app) => !string.IsNullOrWhiteSpace(app.IdentityPath)
        ? app.IdentityPath : !string.IsNullOrWhiteSpace(app.Launch) ? app.Launch : app.Folder;

    // macOS can use case-sensitive volumes. Do not assume case folding there.
    public static StringComparison Comparison(OsKind? platform = null) =>
        (platform ?? Os.Kind) == OsKind.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string Id(AppEntry app)
    {
        // IdentityPath is resolved once on insertion. Do not follow a later retargeted symlink
        // or require the application to remain installed to focus its existing card.
        var path = Normalize(PathOf(app), resolveAliases: false);
        if (Os.IsWindows) path = path.ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))).ToLowerInvariant();
    }

    public static bool SamePath(string a, string b, OsKind? platform = null) =>
        string.Equals(Normalize(a, platform), Normalize(b, platform), Comparison(platform));

    public static AppEntry? Find(IEnumerable<AppEntry> apps, string path) =>
        apps.FirstOrDefault(app => SamePath(PathOf(app), path));

    public static string Normalize(string path, OsKind? platform = null, bool resolveAliases = true)
    {
        path = path.Trim().Trim('"');
        if (path.Length == 0) return "";
        var kind = platform ?? Os.Kind;
        try
        {
            if (kind == OsKind.Windows && !Os.IsWindows)
                return NormalizeWindowsFixture(path);
            var full = Path.GetFullPath(path);
            if (resolveAliases && kind == Os.Kind && (File.Exists(full) || Directory.Exists(full)))
                full = Os.IsWindows ? ResolveWindowsLinks(full) : Os.RealPath(full);
            return Path.TrimEndingDirectorySeparator(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Unresolvable input remains distinct. Never guess a shortcut target or execute it.
            return path;
        }
    }

    private static string ResolveWindowsLinks(string full)
    {
        var root = Path.GetPathRoot(full)!;
        var parts = full[root.Length..].Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 256) return full;
        var current = root;
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is null) continue;
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target is null || !target.Exists) return full;
            current = target.FullName;
        }
        return current;
    }

    // Allows platform path-policy tests without asking the host filesystem to interpret
    // a foreign drive letter. Production Windows always uses Path.GetFullPath above.
    private static string NormalizeWindowsFixture(string path)
    {
        path = path.Replace('/', '\\');
        var rooted = path.StartsWith("\\\\", StringComparison.Ordinal);
        var prefix = rooted ? "\\\\" : path.Length >= 2 && path[1] == ':' ? path[..2] + "\\" : "";
        var tail = rooted ? path[2..] : prefix.Length > 0 ? path[2..] : path;
        var parts = new List<string>();
        foreach (var part in tail.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == ".." && parts.Count > (rooted ? 2 : 0)) parts.RemoveAt(parts.Count - 1);
            else if (part != "..") parts.Add(part);
        }
        return prefix + string.Join('\\', parts);
    }
}
