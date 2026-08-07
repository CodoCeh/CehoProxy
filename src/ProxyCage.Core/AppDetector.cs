using System.Text.RegularExpressions;

namespace ProxyCage.Core;

/// <summary>
/// Пользователь указывает исполняемый файл — мы сами определяем, что изолировать.
/// По ПАПКЕ, а не по файлу: приложения на Electron/Chromium поднимают вспомогательные
/// процессы с другими именами, и правило по одному файлу их пропустит.
///
/// Главная опасность здесь — подняться слишком высоко. Программа из /usr/bin дала бы
/// папку /usr/bin, и под изоляцию уехала бы половина системы. Поэтому системные каталоги
/// каждой ОС перечислены явно, и для них правило строится по одному файлу.
/// </summary>
public static class AppDetector
{
    /// <summary>Служебные подпапки, внутри которых лежит исполняемый файл, а изолировать надо родителя.</summary>
    private static readonly string[] NestedDirs =
        { "app", "bin", "sbin", "lib", "libexec", "resources", "current", "files", "app-*" };

    /// <summary>MSIX/Store: ...\WindowsApps\Publisher.Name_1.2.3.4_x64__hash\ — версия меняется при обновлении.</summary>
    private static readonly Regex MsixVersioned = new(
        @"^(?<prefix>.*\\WindowsApps\\[^\\]+?)_\d+(\.\d+)*(?<suffix>_[^\\]*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public sealed record Detection(
        string Folder,
        string Name,
        bool VersionAgnostic,
        bool SingleFile,
        string Explanation);

    /// <summary>Каталоги, которые нельзя изолировать целиком ни при каких условиях.</summary>
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

    /// <summary>
    /// Определяет, что попадёт под изоляцию. Возвращает и человеческое объяснение —
    /// его показываем до подтверждения, чтобы пользователь видел результат, а не догадывался.
    /// </summary>
    public static Detection Detect(string exeOrFolderPath, string lang = "ru")
    {
        var entered = Path.GetFullPath(exeOrFolderPath.Trim().Trim('"'));
        // именно физический путь: по нему движок опознаёт процесс, а не по тому, что ввели
        var full = Os.RealPath(entered);
        var isFile = File.Exists(full);

        // путь после ссылок может не совпасть с введённым — объясняем, иначе человек
        // увидит в списке незнакомый путь и решит, что программа поняла его неправильно
        var resolvedNote = full.Equals(entered, StringComparison.Ordinal)
            ? ""
            : Strings.T(lang, "det_resolved");

        // .app — это и есть граница приложения на macOS: внутри и бинарник, и все хелперы
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
            var name = Path.GetFileName(msix.Groups["prefix"].Value);
            return new Detection(
                folder, name, true, false,
                Strings.T(lang, "det_msix", name) + climbedNote + resolvedNote);
        }

        return new Detection(
            folder, NiceName(folder, entered), false, false,
            Strings.T(lang, "det_folder") + climbedNote + resolvedNote);
    }

    /// <summary>
    /// Понятное имя для списка. Имя папки часто оказывается номером версии
    /// (/opt/homebrew/Cellar/curl/8.20.0), и запись «8.20.0» человеку ничего не говорит.
    /// </summary>
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

    /// <summary>/Applications/Foo.app/Contents/MacOS/Foo → /Applications/Foo.app</summary>
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

            // корень MSIX-пакета трогать нельзя — иначе поднимемся в общий WindowsApps
            if (parent.EndsWith(@"\WindowsApps", StringComparison.OrdinalIgnoreCase)) break;
            if (IsSystemFolder(parent)) break;

            var isNested = NestedDirs.Any(d => d.EndsWith('*')
                ? leaf.StartsWith(d.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)
                : leaf.Equals(d, StringComparison.OrdinalIgnoreCase));
            if (!isNested) break;

            current = parent;
        }
        return current;
    }

    /// <summary>
    /// Go-regex, матчащий процессы приложения. Для MSIX — по префиксу без версии,
    /// чтобы обновление приложения не выключало изоляцию молча.
    /// </summary>
    public static string ToRegex(AppEntry app)
    {
        var prefix = Os.IsLinux ? "^" : "(?i)^";   // на Linux пути регистрозависимы

        if (app.SingleFile)
            return prefix + EscapeGo(app.Folder) + "$";

        var folder = app.Folder.TrimEnd('\\', '/');

        if (app.VersionAgnostic && MsixVersioned.Match(folder) is { Success: true } m)
            return prefix + EscapeGo(m.Groups["prefix"].Value) + @"_[^\\]*[\\/]";

        return prefix + EscapeGo(folder) + (Os.IsWindows ? @"[\\/]" : "/");
    }

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
