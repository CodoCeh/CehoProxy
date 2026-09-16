using Microsoft.Win32;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace ProxyCage.Core;

public static class InstalledAppCatalog
{
    public sealed record Entry(string Name, string Path, string Source);
    private static readonly object CacheGate = new();
    private static IReadOnlyList<Entry>? _cached;
    private static DateTime _cachedAtUtc;
    private static string? _cachedLang;

    public static IReadOnlyList<Entry> Detect(string lang = "ru")
    {
        lock (CacheGate)
            if (_cached is not null && _cachedLang == lang
                && DateTime.UtcNow - _cachedAtUtc < TimeSpan.FromMinutes(5))
                return _cached;

        IEnumerable<Entry> entries = Os.Kind switch
        {
            OsKind.Windows => DetectWindows(),
            OsKind.Mac => DetectMac(),
            _ => DetectLinux(lang),
        };

        var detected = entries
            .Where(e => e.Name.Length > 0 && e.Path.Length > 0)
            .GroupBy(e => NormalizePath(e.Path), Os.IsLinux
                ? StringComparer.Ordinal
                : StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(e => e.Name.Length).First())
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.Path, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        lock (CacheGate)
        {
            _cached = detected;
            _cachedAtUtc = DateTime.UtcNow;
            _cachedLang = lang;
        }
        return detected;
    }

    private static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return path; }
    }

    private static IEnumerable<Entry> DetectWindows()
    {
        if (!OperatingSystem.IsWindows()) yield break;

        var roots = new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Registry64),
            (RegistryHive.CurrentUser, RegistryView.Registry32),
        };

        foreach (var (hive, view) in roots)
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) continue;
            foreach (var entry in ReadWindowsUninstall(uninstall)) yield return entry;
        }

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var users = RegistryKey.OpenBaseKey(RegistryHive.Users, view);
            foreach (var sid in users.GetSubKeyNames().Where(s => s.StartsWith("S-1-5-21-", StringComparison.Ordinal)))
            {
                using var uninstall = users.OpenSubKey(
                    $@"{sid}\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var entry in ReadWindowsUninstall(uninstall)) yield return entry;
            }
        }

        foreach (var (hive, view) in roots)
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var paths = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
            if (paths is null) continue;
            foreach (var id in paths.GetSubKeyNames())
            {
                using var app = paths.OpenSubKey(id);
                var path = CleanWindowsExecutable(app?.GetValue(null) as string);
                if (path is not null && File.Exists(path))
                    yield return new(Path.GetFileNameWithoutExtension(id), path, "Windows");
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<Entry> ReadWindowsUninstall(RegistryKey uninstall)
    {
        foreach (var id in uninstall.GetSubKeyNames())
        {
            using var app = uninstall.OpenSubKey(id);
            if (app?.GetValue("SystemComponent") is int system && system == 1) continue;
            var name = (app?.GetValue("DisplayName") as string)?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var icon = CleanWindowsExecutable(app?.GetValue("DisplayIcon") as string);
            if (icon is not null && File.Exists(icon))
            {
                yield return new(name, icon, "Windows");
                continue;
            }

            var location = (app?.GetValue("InstallLocation") as string)?.Trim().Trim('"');
            var executable = FindLikelyExecutable(location, name);
            if (executable is not null) yield return new(name, executable, "Windows");
        }
    }

    internal static string? CleanWindowsExecutable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = Environment.ExpandEnvironmentVariables(value.Trim());
        var quoted = Regex.Match(text, "^\\\"(?<path>[^\\\"]+\\.exe)\\\"");
        if (quoted.Success) return quoted.Groups["path"].Value;
        var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe < 0 ? null : text[..(exe + 4)].Trim().Trim('"');
    }

    private static string? FindLikelyExecutable(string? folder, string displayName)
    {
        if (string.IsNullOrWhiteSpace(folder) || !IsLocalFixedPath(folder) || !Directory.Exists(folder)) return null;
        try
        {
            var files = Directory.EnumerateFiles(folder, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(p => !Path.GetFileName(p).Equals("uninstall.exe", StringComparison.OrdinalIgnoreCase))
                .Take(30).ToList();
            if (files.Count == 0) return null;
            var key = Regex.Replace(displayName, "[^a-z0-9]", "", RegexOptions.IgnoreCase);
            return files.FirstOrDefault(p =>
                       key.Contains(Regex.Replace(Path.GetFileNameWithoutExtension(p), "[^a-z0-9]", "",
                           RegexOptions.IgnoreCase), StringComparison.OrdinalIgnoreCase))
                   ?? files[0];
        }
        catch { return null; }
    }

    private static bool IsLocalFixedPath(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch { return false; }
    }

    private static IEnumerable<Entry> DetectMac()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var root in new[]
        {
            "/Applications", "/System/Applications", "/System/Applications/Utilities",
            Path.Combine(home, "Applications"),
        })
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> bundles;
            try { bundles = Directory.EnumerateDirectories(root, "*.app", SearchOption.TopDirectoryOnly).ToList(); }
            catch { continue; }
            foreach (var bundle in bundles)
                yield return new(Path.GetFileNameWithoutExtension(bundle), bundle, "macOS");
        }
    }

    private static IEnumerable<Entry> DetectLinux(string lang)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var root in new[]
        {
            "/usr/share/applications", "/usr/local/share/applications",
            Path.Combine(home, ".local/share/applications"),
            "/var/lib/flatpak/exports/share/applications",
            Path.Combine(home, ".local/share/flatpak/exports/share/applications"),
        })
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*.desktop", SearchOption.TopDirectoryOnly).ToList(); }
            catch { continue; }
            foreach (var file in files)
            {
                Entry? entry;
                try { entry = ParseDesktopEntry(File.ReadAllText(file), FindOnPath, lang); }
                catch { continue; }
                if (entry is not null) yield return entry;
            }
        }
    }

    internal static Entry? ParseDesktopEntry(string text, Func<string, string?> resolve, string lang)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inEntry = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.StartsWith('['))
            {
                inEntry = line.Equals("[Desktop Entry]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inEntry || line.Length == 0 || line.StartsWith('#')) continue;
            var equal = line.IndexOf('=');
            if (equal > 0) values[line[..equal]] = line[(equal + 1)..].Trim();
        }

        if (!values.GetValueOrDefault("Type", "Application").Equals("Application", StringComparison.OrdinalIgnoreCase)
            || IsTrue(values.GetValueOrDefault("Hidden")) || IsTrue(values.GetValueOrDefault("NoDisplay")))
            return null;

        var name = lang.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
            ? values.GetValueOrDefault("Name[ru]", values.GetValueOrDefault("Name", ""))
            : values.GetValueOrDefault("Name", "");
        var command = values.GetValueOrDefault("TryExec", "");
        if (command.Length == 0) command = ExecutableCommand(values.GetValueOrDefault("Exec", ""));
        if (name.Length == 0 || command.Length == 0) return null;
        var path = resolve(command);
        return path is null ? null : new(name, path, "Linux");
    }

    private static bool IsTrue(string? value) =>
        value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

    internal static string FirstCommand(string exec)
    {
        exec = exec.Trim();
        if (exec.Length == 0) return "";
        if (exec[0] == '"')
        {
            var end = exec.IndexOf('"', 1);
            return end > 1 ? exec[1..end] : "";
        }
        var space = exec.IndexOfAny([' ', '\t']);
        return space < 0 ? exec : exec[..space];
    }

    internal static string ExecutableCommand(string exec)
    {
        var first = FirstCommand(exec);
        if (!Path.GetFileName(first).Equals("env", StringComparison.OrdinalIgnoreCase))
            return IsSharedLauncher(first) ? "" : first;

        var rest = exec.Trim()[first.Length..].TrimStart();
        while (rest.Length > 0)
        {
            var candidate = FirstCommand(rest);
            if (candidate.Length == 0) return "";
            rest = rest[candidate.Length..].TrimStart();
            if (candidate.Contains('=') && !candidate.Contains('/')) continue;
            return IsSharedLauncher(candidate) ? "" : candidate;
        }
        return "";
    }

    private static bool IsSharedLauncher(string command) =>
        Path.GetFileName(command) is "flatpak" or "gtk-launch" or "sh" or "bash";

    private static string? FindOnPath(string command)
    {
        if (Path.IsPathRooted(command)) return File.Exists(command) ? command : null;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var path = Path.Combine(dir, command);
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
