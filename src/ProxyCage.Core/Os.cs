using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ProxyCage.Core;

public enum OsKind { Windows, Linux, Mac }

/// <summary>
/// Всё, что расходится между Windows, Linux и macOS: пути, права, поиск внешних программ.
/// Платформенный шов собран здесь и в четырёх соседних файлах
/// (SingBoxProcess, TunCleanup, Autostart, AppDetector) — больше нигде.
/// </summary>
public static class Os
{
    public static OsKind Kind =>
        OperatingSystem.IsWindows() ? OsKind.Windows :
        OperatingSystem.IsMacOS() ? OsKind.Mac : OsKind.Linux;

    public static bool IsWindows => Kind == OsKind.Windows;
    public static bool IsMac => Kind == OsKind.Mac;
    public static bool IsLinux => Kind == OsKind.Linux;

    /// <summary>Папка настроек по умолчанию. Каноничное для каждой системы место, доступное только root.</summary>
    public static string DefaultRoot => Kind switch
    {
        OsKind.Windows => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CehoProxy"),
        OsKind.Mac => "/Library/Application Support/CehoProxy",
        _ => "/var/lib/cehoproxy",
    };

    public static string SingBoxFileName => IsWindows ? "sing-box.exe" : "sing-box";

    /// <summary>
    /// Движок ищем в папке настроек, рядом с собой и в PATH. На Unix его обычно ставят
    /// пакетным менеджером, и требовать копию в своей папке — лишний тупик на ровном месте.
    /// </summary>
    public static string? ResolveSingBox(string root)
    {
        var candidates = new List<string> { Path.Combine(root, SingBoxFileName) };

        var own = Path.GetDirectoryName(Environment.ProcessPath ?? "");
        if (!string.IsNullOrEmpty(own)) candidates.Add(Path.Combine(own, SingBoxFileName));

        candidates.AddRange(IsWindows
            ? new[] { "" }
            : new[] { "/usr/local/bin", "/usr/bin", "/opt/homebrew/bin", "/opt/sing-box/bin" }
                .Select(d => Path.Combine(d, SingBoxFileName)));

        foreach (var c in candidates.Where(c => c.Length > 0))
            if (File.Exists(c)) return c;

        return FindOnPath(SingBoxFileName);
    }

    /// <summary>curl нужен для проб выхода — HttpClient через локальный вход sing-box глухо таймаутит.</summary>
    public static string? ResolveCurl()
    {
        if (IsWindows)
        {
            var sys = Path.Combine(Environment.SystemDirectory, "curl.exe");
            return File.Exists(sys) ? sys : FindOnPath("curl.exe");
        }
        foreach (var c in new[] { "/usr/bin/curl", "/bin/curl", "/usr/local/bin/curl", "/opt/homebrew/bin/curl" })
            if (File.Exists(c)) return c;
        return FindOnPath("curl");
    }

    public static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = Path.Combine(dir.Trim(), fileName);
                if (IsRunnable(full)) return full;
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// Файл есть И его можно запустить.
    ///
    /// Одного File.Exists мало: битая символическая ссылка проходит эту проверку, и продукт
    /// объявлял установленной программу, которой в системе нет. Поймано живьём на macOS —
    /// /usr/local/bin/cursor вёл на давно отмонтированный том установщика.
    /// </summary>
    public static bool IsRunnable(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            var target = RealPath(path);
            if (!File.Exists(target)) return false;          // ссылка в никуда
            if (IsWindows) return true;

            var mode = File.GetUnixFileMode(target);
            return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEuid();

    [DllImport("libc", EntryPoint = "realpath", SetLastError = true)]
    private static extern IntPtr RealPathNative(string path, IntPtr resolved);

    [DllImport("libc", EntryPoint = "free")]
    private static extern void FreeNative(IntPtr ptr);

    /// <summary>
    /// Физический путь, без символических ссылок.
    ///
    /// Это не косметика: ядро macOS отдаёт движку именно физический путь. Приложение,
    /// добавленное как /tmp/app, в системе выглядит как /private/tmp/app, и правило
    /// изоляции по исходному пути не срабатывает МОЛЧА — продукт рапортует «изолировано»,
    /// а трафик идёт мимо. Поймано живьём на macOS.
    /// </summary>
    public static string RealPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (IsWindows) return full;

        var ptr = IntPtr.Zero;
        try
        {
            ptr = RealPathNative(full, IntPtr.Zero);
            return ptr == IntPtr.Zero ? full : Marshal.PtrToStringUTF8(ptr) ?? full;
        }
        catch
        {
            return full;
        }
        finally
        {
            if (ptr != IntPtr.Zero) FreeNative(ptr);
        }
    }

    public static bool IsElevated()
    {
        try
        {
            return OperatingSystem.IsWindows() ? WindowsElevated() : GetEuid() == 0;
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool WindowsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    public static (int Code, string Output) Run(string file, string args, int timeoutMs = 30000)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return (-1, "процесс не запустился");
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return (-1, "не дождались завершения"); }
            return (p.ExitCode, (stdout + stderr).Trim());
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    /// <summary>Открыть панель в браузере. Единственное место, где нужен GUI-хост системы.</summary>
    public static void OpenInBrowser(string url)
    {
        var (file, args) = Kind switch
        {
            OsKind.Windows => ("cmd", $"/c start \"\" \"{url}\""),
            OsKind.Mac => ("open", url),
            _ => ("xdg-open", url),
        };
        try { Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true }); }
        catch { }
    }
}
