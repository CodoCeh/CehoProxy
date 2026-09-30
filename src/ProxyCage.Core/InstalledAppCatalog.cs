using Microsoft.Win32;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace ProxyCage.Core;

public static class InstalledAppCatalog
{
    public sealed record Entry(string Name, string Path, string Source, string? Icon = null);

    public enum Group { Browsers, Messengers, Ai, Other }

    private static readonly string[] MessengerWords =
        { "telegram", "whatsapp", "discord", "signal", "viber", "slack", "teams", "zoom", "skype", "element", "threema" };

    private static readonly string[] AiWords =
        { "chatgpt", "claude", "codex", "cursor", "gemini", "copilot", "antigravity", "windsurf", "perplexity", "ollama", "lm studio", "deepseek", "grok" };

    public static Group GroupOf(Entry entry)
    {
        var name = entry.Name.ToLowerInvariant();
        bool Word(string w) => System.Text.RegularExpressions.Regex.IsMatch(name, $@"(^|[^\p{{L}}]){System.Text.RegularExpressions.Regex.Escape(w)}($|[^\p{{L}}])");
        if (AiWords.Any(Word)) return Group.Ai;
        if (MessengerWords.Any(Word)) return Group.Messengers;
        if (AppDetector.IsBrowser(new AppEntry { Name = entry.Name, Folder = entry.Path })) return Group.Browsers;
        return Group.Other;
    }
    private static readonly object CacheGate = new();
    private static IReadOnlyList<Entry>? _cached;
    private static DateTime _cachedAtUtc;
    private static string? _cachedLang;

    public static IReadOnlyList<Entry> Detect(string lang = "ru", bool fresh = false)
    {
        lock (CacheGate)
            if (!fresh && _cached is not null && _cachedLang == lang
                && DateTime.UtcNow - _cachedAtUtc < TimeSpan.FromMinutes(5))
                return _cached;

        IEnumerable<Entry> entries = Os.Kind switch
        {
            OsKind.Windows => DetectWindows(),
            OsKind.Mac => DetectMac(),
            _ => DetectLinux(lang),
        };

        var self = Environment.ProcessPath;
        var detected = entries
            .Where(e => e.Name.Length > 0 && e.Path.Length > 0 && !IsOwn(e.Path, self, Os.IsWindows))
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

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, view) in roots)
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) continue;
            foreach (var entry in ReadWindowsUninstall(uninstall)) { seen.Add(NormalizePath(entry.Path)); yield return entry; }
        }

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var users = RegistryKey.OpenBaseKey(RegistryHive.Users, view);
            foreach (var sid in users.GetSubKeyNames().Where(s => s.StartsWith("S-1-5-21-", StringComparison.Ordinal)))
            {
                using var uninstall = users.OpenSubKey(
                    $@"{sid}\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var entry in ReadWindowsUninstall(uninstall)) { seen.Add(NormalizePath(entry.Path)); yield return entry; }
            }
        }

        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var storeDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");

        foreach (var (hive, view) in roots)
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var paths = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
            if (paths is null) continue;
            foreach (var id in paths.GetSubKeyNames())
            {
                using var app = paths.OpenSubKey(id);
                var path = CleanWindowsExecutable(app?.GetValue(null) as string);
                if (path is not null && File.Exists(path) && !IsUnder(path, windowsDir) && !IsUnder(path, storeDir) && !IsWindowsComponent(ProductOf(path))
                    && !SameProgramSeen(seen, path) && seen.Add(NormalizePath(path)))
                    yield return new(Path.GetFileNameWithoutExtension(id), path, "Windows");
            }
        }

        foreach (var entry in DetectWindowsStore(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps")))
            yield return entry;
    }

    internal static IEnumerable<Entry> DetectWindowsStore(string root)
    {
        List<string> packages;
        try { packages = Directory.EnumerateDirectories(root).ToList(); }
        catch { yield break; }

        var groups = packages
            .Select(dir => (Dir: dir, Parts: Path.GetFileName(dir).Split('_')))
            .Where(p => p.Parts.Length >= 2)
            .GroupBy(p => p.Parts[0], StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var entry = group
                .OrderByDescending(p => Version.TryParse(p.Parts[1], out var v) ? v : new Version())
                .Select(p => ReadStoreManifest(p.Dir, p.Parts[0]))
                .FirstOrDefault(e => e is not null);
            if (entry is not null) yield return entry;
        }
    }

    internal static string? WithoutTrailingVersion(string? name)
    {
        if (name is null) return null;
        var cut = Regex.Replace(name, @"\s+v?\d+(\.\d+){1,3}$", "", RegexOptions.IgnoreCase).Trim();
        return cut.Length > 0 ? cut : name;
    }

    private static string? ProductOf(string path)
    {
        try { return System.Diagnostics.FileVersionInfo.GetVersionInfo(path).ProductName; }
        catch { return null; }
    }

    internal static bool IsWindowsComponent(string? product) =>
        product is not null
        && (product.Trim().Equals("Internet Explorer", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(product, @"^Microsoft\W*Windows\W*Operating System$", RegexOptions.IgnoreCase));

    internal static bool SameProgramSeen(IEnumerable<string> seen, string path)
    {
        var name = Path.GetFileName(path);
        var dir = Path.GetDirectoryName(path) ?? "";
        return seen.Any(p => Path.GetFileName(p).Equals(name, StringComparison.OrdinalIgnoreCase)
            && (NormalizePath(p).Equals(NormalizePath(path), StringComparison.OrdinalIgnoreCase)
                || IsUnder(p, dir) || IsUnder(path, Path.GetDirectoryName(p) ?? "")));
    }

    internal static bool IsOwn(string path, string? self, bool windows)
    {
        if (Path.GetFileName(path.TrimEnd('/', '\\')).StartsWith("CehoProxy", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.IsNullOrEmpty(self)) return false;
        if (NormalizePath(path).Equals(NormalizePath(self), StringComparison.OrdinalIgnoreCase) || IsUnder(self, path)) return true;
        return windows && IsUnder(path, Path.GetDirectoryName(self) ?? "");
    }

    internal static bool IsUnder(string path, string folder) =>
        folder.Length > 0 && NormalizePath(path).StartsWith(NormalizePath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    internal static Entry? ReadStoreManifest(string dir, string packageName)
    {
        System.Xml.Linq.XDocument doc;
        try { doc = System.Xml.Linq.XDocument.Load(Path.Combine(dir, "AppxManifest.xml")); }
        catch { return null; }

        var all = doc.Descendants().ToList();
        if (all.Any(e => e.Name.LocalName == "Framework" && e.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)))
            return null;

        var app = all.FirstOrDefault(e => e.Name.LocalName == "Application"
            && ((string?)e.Attribute("Executable"))?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true
            && !string.Equals((string?)e.Descendants().FirstOrDefault(v => v.Name.LocalName == "VisualElements")?.Attribute("AppListEntry"), "none", StringComparison.OrdinalIgnoreCase));
        if (app is null) return null;

        var exe = Path.Combine(dir, ((string)app.Attribute("Executable")!).Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(exe)) return null;

        var name = new[]
            {
                all.FirstOrDefault(e => e.Name.LocalName == "DisplayName" && e.Parent?.Name.LocalName == "Properties")?.Value,
                (string?)app.Descendants().FirstOrDefault(e => e.Name.LocalName == "VisualElements")?.Attribute("DisplayName"),
            }
            .Select(n => n?.Trim())
            .FirstOrDefault(n => !string.IsNullOrEmpty(n) && !n.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            var publisher = (string?)all.FirstOrDefault(e => e.Name.LocalName == "Identity")?.Attribute("Publisher") ?? "";
            if (publisher.Contains("O=Microsoft Corporation", StringComparison.OrdinalIgnoreCase)) return null;
            name = packageName[(packageName.LastIndexOf('.') + 1)..];
        }

        return new(name, exe, "Microsoft Store");
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<Entry> ReadWindowsUninstall(RegistryKey uninstall)
    {
        foreach (var id in uninstall.GetSubKeyNames())
        {
            using var app = uninstall.OpenSubKey(id);
            if (app?.GetValue("SystemComponent") is int system && system == 1) continue;
            var name = WithoutTrailingVersion((app?.GetValue("DisplayName") as string)?.Trim());
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

    internal static string? FindLikelyExecutable(string? folder, string displayName)
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

    private static IEnumerable<string> UserHomes(string usersRoot) =>
        UserHomes(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Os.IsElevated(), usersRoot);

    internal static IEnumerable<string> UserHomes(string currentHome, bool elevated, string usersRoot)
    {
        var homes = new List<string>();
        if (currentHome.Length > 0) homes.Add(currentHome);
        if (elevated && Directory.Exists(usersRoot))
        {
            try
            {
                homes.AddRange(Directory.EnumerateDirectories(usersRoot)
                    .Where(d => !Path.GetFileName(d).Equals("Shared", StringComparison.OrdinalIgnoreCase)
                        && !Path.GetFileName(d).StartsWith('.')));
            }
            catch { }
        }
        return homes.Distinct(StringComparer.Ordinal);
    }

    private static IEnumerable<Entry> DetectMac()
    {
        foreach (var root in new[] { "/Applications", "/System/Applications", "/System/Applications/Utilities" }
            .Concat(UserHomes("/Users").Select(h => Path.Combine(h, "Applications"))))
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
        foreach (var root in new[]
        {
            "/usr/share/applications", "/usr/local/share/applications",
            "/var/lib/flatpak/exports/share/applications",
        }.Concat(UserHomes("/home").SelectMany(h => new[]
        {
            Path.Combine(h, ".local/share/applications"),
            Path.Combine(h, ".local/share/flatpak/exports/share/applications"),
        })))
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
        if (path is null) return null;
        var icon = values.GetValueOrDefault("Icon", "").Trim();
        return new(name, path, "Linux", icon.Length == 0 ? null : icon);
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
