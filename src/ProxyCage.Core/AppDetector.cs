using System.Text.RegularExpressions;

namespace ProxyCage.Core;

public static class AppDetector
{
    private static readonly string[] NestedDirs =
        { "app", "bin", "sbin", "lib", "libexec", "resources", "current", "files", "app-*" };

    private static readonly Regex MsixVersioned = new(
        @"^(?<prefix>.*\\WindowsApps\\[^\\]+?)_\d+(\.\d+)*(?<suffix>_[^\\]*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public sealed record Detection(
        string Folder,
        string Name,
        bool VersionAgnostic,
        bool SingleFile,
        string Explanation);

    private static IEnumerable<string> SystemFolders()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        switch (Os.Kind)
        {
            case OsKind.Windows:
                yield return @"C:\";
                yield return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                yield return Environment.GetFolderPath(Environment.SpecialFolder.System);
                yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                yield return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
                break;

            case OsKind.Mac:
                foreach (var d in new[]
                {
                    "/", "/Applications", "/System", "/System/Applications", "/Library", "/usr", "/usr/bin",
                    "/usr/sbin", "/usr/local", "/usr/local/bin", "/usr/libexec", "/bin", "/sbin",
                    "/opt", "/opt/homebrew", "/opt/homebrew/bin", "/Users", "/private", "/tmp", "/var",
                }) yield return d;
                break;

            default:
                foreach (var d in new[]
                {
                    "/", "/usr", "/usr/bin", "/usr/sbin", "/usr/local", "/usr/local/bin", "/usr/local/sbin",
                    "/usr/lib", "/usr/libexec", "/usr/share", "/bin", "/sbin", "/lib", "/lib64",
                    "/opt", "/snap", "/var", "/var/lib", "/etc", "/home", "/tmp", "/srv", "/run",
                }) yield return d;
                break;
        }
        if (!string.IsNullOrEmpty(home)) yield return home;
    }

    private static bool IsSystemFolder(string folder) =>
        SystemFolders().Any(s => !string.IsNullOrEmpty(s) && SamePath(s, folder));

    private static bool SamePath(string a, string b)
    {
        a = a.TrimEnd('\\', '/'); b = b.TrimEnd('\\', '/');
        if (a.Length == 0) a = "/";
        if (b.Length == 0) b = "/";
        return string.Equals(a, b, Os.IsLinux ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }

    public static Detection Detect(string exeOrFolderPath, string lang = "ru")
    {
        var entered = Path.GetFullPath(exeOrFolderPath.Trim().Trim('"'));

        var full = Os.RealPath(entered);
        var isFile = File.Exists(full);

        var resolvedNote = full.Equals(entered, StringComparison.Ordinal)
            ? ""
            : Strings.T(lang, "det_resolved");

        if (Os.IsMac && BundleRoot(full) is { } bundle)
        {
            return new Detection(
                bundle, Path.GetFileNameWithoutExtension(bundle), false, false,
                Strings.T(lang, "det_bundle") + resolvedNote);
        }

        var folder = Directory.Exists(full) ? full : Path.GetDirectoryName(full) ?? full;
        folder = folder.TrimEnd('\\', '/');
        if (folder.Length == 0) folder = "/";

        var climbed = ClimbOutOfNestedDir(folder);
        var climbedNote = climbed != folder ? Strings.T(lang, "det_climbed") : "";
        folder = climbed;

        if (IsSystemFolder(folder))
        {
            if (!isFile)
                throw new InvalidOperationException(Strings.T(lang, "det_refuse_system_dir", folder));

            return new Detection(
                full, Path.GetFileNameWithoutExtension(full), false, true,
                Strings.T(lang, "det_system_dir", folder) + resolvedNote);
        }

        if (Os.IsWindows && MsixVersioned.Match(folder) is { Success: true } msix)
        {
            var name = NormalizeMsixName(Path.GetFileName(msix.Groups["prefix"].Value));
            return new Detection(
                folder, name, true, false,
                Strings.T(lang, "det_msix", name) + climbedNote + resolvedNote);
        }

        return new Detection(
            folder, NiceName(folder, entered), false, false,
            Strings.T(lang, "det_folder") + climbedNote + resolvedNote);
    }

    private static string NiceName(string folder, string entered)
    {
        var leaf = Path.GetFileName(folder);
        if (!LooksLikeVersion(leaf)) return leaf;

        var parent = Path.GetFileName(Path.GetDirectoryName(folder) ?? "");
        if (parent.Length > 0 && !LooksLikeVersion(parent)) return parent;

        var entry = Path.GetFileNameWithoutExtension(entered);
        return entry.Length > 0 ? entry : leaf;
    }

    private static bool LooksLikeVersion(string name) =>
        name.Length > 0 && char.IsDigit(name[0]) && name.All(c => char.IsDigit(c) || c is '.' or '-' or '_');

    private static string? BundleRoot(string path)
    {
        var current = path.TrimEnd('/');
        while (current.Length > 1)
        {
            if (current.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return current;
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || parent == current) return null;
            current = parent;
        }
        return null;
    }

    private static string ClimbOutOfNestedDir(string folder)
    {
        var current = folder;
        for (var i = 0; i < 3; i++)
        {
            var leaf = Path.GetFileName(current);
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(leaf) || string.IsNullOrEmpty(parent)) break;

            if (parent.EndsWith(@"\WindowsApps", StringComparison.OrdinalIgnoreCase)) break;
            if (IsSystemFolder(parent)) break;

            var isNested = NestedDirs.Any(d => d.EndsWith('*')
                ? leaf.StartsWith(d.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)
                : leaf.Equals(d, StringComparison.OrdinalIgnoreCase))
                || IsHexHash(leaf);
            if (!isNested) break;

            current = parent;
        }
        return current;
    }

    private static bool IsHexHash(string s) =>
        s.Length >= 8 && s.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

    public static string ToRegex(AppEntry app) => ToRegexes(app)[0];

    public static IReadOnlyList<string> ToRegexes(AppEntry app)
    {
        var isWinPath = Os.IsWindows || app.Folder.Contains('\\') || (app.Folder.Length >= 2 && app.Folder[1] == ':');
        var prefix = (!isWinPath && Os.IsLinux) ? "^" : "(?i)^";
        var sep = isWinPath ? @"[\\/]" : "/";

        string primary;
        if (app.SingleFile)
        {
            primary = prefix + EscapeGo(app.Folder) + "$";
        }
        else
        {
            var folder = app.Folder.TrimEnd('\\', '/');

            if (app.VersionAgnostic && MsixVersioned.Match(folder) is { Success: true } m)
                primary = prefix + EscapeGo(m.Groups["prefix"].Value) + @"_[^\\]*[\\/]";
            else if (folder.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var lastSep = folder.LastIndexOfAny(new[] { '\\', '/' });
                var dir = lastSep >= 0 ? folder[..lastSep] : folder;
                primary = prefix + "(?:" + EscapeGo(folder) + "$|" + EscapeGo(dir) + sep + ")";
            }
            else
            {
                primary = prefix + EscapeGo(folder) + sep;
            }
        }

        var results = new List<string> { primary };

        void AddIfMissing(string rx)
        {
            if (!results.Contains(rx, StringComparer.OrdinalIgnoreCase))
                results.Add(rx);
        }

        if (IsCodex(app))
        {
            if (isWinPath)
            {
                AddIfMissing(@"(?i)^.*[\\/]AppData[\\/]Local[\\/]OpenAI[\\/]Codex[\\/]");
                AddIfMissing(@"(?i)^.*[\\/]WindowsApps[\\/]OpenAI\.Codex_[^\\/]*[\\/]");
                AddIfMissing(@"(?i)^.*[\\/]AppData[\\/]Local[\\/]Programs[\\/]codex[\\/]");
                AddIfMissing(@"(?i)^.*[\\/](?:codex|ChatGPT)\.exe$");
            }
            else
            {
                AddIfMissing(@"(?i)^.*[\\/]\.codex[\\/]");
                AddIfMissing(@"^.*[\\/]codex$");
            }
        }
        else if (IsCursor(app))
        {
            if (isWinPath)
            {
                AddIfMissing(@"(?i)^.*[\\/]AppData[\\/]Local[\\/]Programs[\\/][Cc]ursor[\\/]");
                AddIfMissing(@"(?i)^.*[\\/]\.cursor[\\/]");
                AddIfMissing(@"(?i)^.*[\\/][Cc]ursor\.exe$");
            }
            else
            {
                AddIfMissing(@"(?i)^.*[\\/]\.cursor[\\/]");
                AddIfMissing(@"^.*[\\/]cursor$");
            }
        }
        else if (IsClaude(app))
        {
            if (isWinPath)
            {
                AddIfMissing(@"(?i)^.*[\\/]AppData[\\/]Local[\\/]AnthropicClaude[\\/]");
                AddIfMissing(@"(?i)^.*[\\/]AppData[\\/]Local[\\/]Programs[\\/]claude[\\/]");
                AddIfMissing(@"(?i)^.*[\\/]\.claude[\\/]");
                AddIfMissing(@"(?i)^.*[\\/][Cc]laude\.exe$");
            }
            else
            {
                AddIfMissing(@"(?i)^.*[\\/]\.claude[\\/]");
                AddIfMissing(@"^.*[\\/]claude$");
            }
        }

        return results;
    }

    private static string NormalizeMsixName(string name) =>
        name.Equals("OpenAI.Codex", StringComparison.OrdinalIgnoreCase) ? "Codex" : name;

    private static bool IsCodex(AppEntry app) =>
        string.Equals(app.Name, "Codex", StringComparison.OrdinalIgnoreCase) ||
        app.Folder.Contains("OpenAI.Codex", StringComparison.OrdinalIgnoreCase) ||
        app.Folder.Contains(@"OpenAI\Codex", StringComparison.OrdinalIgnoreCase) ||
        app.Folder.Contains("OpenAI/Codex", StringComparison.OrdinalIgnoreCase);

    private static bool IsCursor(AppEntry app) =>
        string.Equals(app.Name, "Cursor", StringComparison.OrdinalIgnoreCase) ||
        app.Folder.Contains(@"\cursor", StringComparison.OrdinalIgnoreCase) ||
        app.Folder.Contains("/cursor", StringComparison.OrdinalIgnoreCase);

    private static bool IsClaude(AppEntry app) =>
        string.Equals(app.Name, "Claude", StringComparison.OrdinalIgnoreCase) ||
        app.Folder.Contains("AnthropicClaude", StringComparison.OrdinalIgnoreCase) ||
        app.Folder.Contains(@"\claude", StringComparison.OrdinalIgnoreCase) ||
        app.Folder.Contains("/claude", StringComparison.OrdinalIgnoreCase);

    private static string EscapeGo(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s)
        {
            if ("\\.+*?()|[]{}^$".Contains(ch)) sb.Append('\\');
            sb.Append(ch);
        }
        return sb.ToString();
    }
}
