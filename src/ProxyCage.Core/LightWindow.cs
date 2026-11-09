using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ProxyCage.Core;

/// <summary>Окно CehoProxy Light: браузер системы в режиме приложения, без вкладок и адресной строки.</summary>
public static class LightWindow
{
    public const int Width = 1120;
    public const int Height = 800;

    public static IReadOnlyList<string> Candidates(OsKind os, Func<string, string?> env, Func<string, string?> onPath) =>
        os switch
        {
            OsKind.Windows => new[]
            {
                Path.Combine(env("ProgramFiles(x86)") ?? "", "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(env("ProgramFiles") ?? "", "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(env("ProgramFiles") ?? "", "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(env("ProgramFiles(x86)") ?? "", "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(env("LocalAppData") ?? "", "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(env("ProgramFiles") ?? "", "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
            },
            OsKind.Mac => new[]
            {
                "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
                "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                "/Applications/Brave Browser.app/Contents/MacOS/Brave Browser",
                "/Applications/Chromium.app/Contents/MacOS/Chromium",
            },
            _ => new[] { "microsoft-edge", "google-chrome", "google-chrome-stable", "chromium", "chromium-browser", "brave-browser" }
                .Select(onPath).OfType<string>().ToArray(),
        };

    public static string? Find(OsKind os, Func<string, string?> env, Func<string, string?> onPath, Func<string, bool> exists) =>
        Candidates(os, env, onPath).FirstOrDefault(exists);

    public static IReadOnlyList<string> Arguments(string url, string profile) => new[]
    {
        $"--app={url}",
        $"--user-data-dir={profile}",
        $"--window-size={Width},{Height}",
        "--no-first-run",
        "--no-default-browser-check",
    };

    private static readonly object Gate = new();
    private static Process? _open;
    private static DateTime _openedAt;
    private static readonly TimeSpan Startup = TimeSpan.FromSeconds(15);

    public static bool Open(string url)
    {
        lock (Gate)
        {
            if (_open is { } live && !Exited(live))
            {
                if (Raise(live)) return true;
                if (DateTime.UtcNow - _openedAt < Startup) return true;
            }
            var exe = Find(Os.Kind, Environment.GetEnvironmentVariable, FromPath, File.Exists);
            if (exe is null)
            {
                Os.OpenInBrowser(url);
                return false;
            }
            try
            {
                var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
                foreach (var arg in Arguments(url, Profile())) info.ArgumentList.Add(arg);
                _open = Process.Start(info);
                _openedAt = DateTime.UtcNow;
                return true;
            }
            catch
            {
                Os.OpenInBrowser(url);
                return false;
            }
        }
    }

    /// <summary>Маленькое окно выбора «Веб-интерфейс или Light». Свой профиль браузера, чтобы закрыть его можно было, не трогая окно Light.</summary>
    public static Process? OpenChooser(string url)
    {
        var exe = Find(Os.Kind, Environment.GetEnvironmentVariable, FromPath, File.Exists);
        if (exe is null) return null;
        try
        {
            var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            info.ArgumentList.Add($"--app={url}");
            info.ArgumentList.Add($"--user-data-dir={Path.Combine(Path.GetDirectoryName(Profile())!, "choose-profile")}");
            info.ArgumentList.Add("--window-size=460,420");
            info.ArgumentList.Add("--no-first-run");
            info.ArgumentList.Add("--no-default-browser-check");
            return Process.Start(info);
        }
        catch { return null; }
    }

    private static string Profile() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CehoProxy", "light-profile");

    private static bool Exited(Process process)
    {
        try { process.Refresh(); return process.HasExited; }
        catch { return true; }
    }

    private static bool Raise(Process process)
    {
        try
        {
            process.Refresh();
            if (process.HasExited) return false;
            if (OperatingSystem.IsWindows())
            {
                var handle = process.MainWindowHandle;
                if (handle == IntPtr.Zero) return false;
                ShowWindow(handle, 9);
                SetForegroundWindow(handle);
                return true;
            }
            if (OperatingSystem.IsLinux()) return Activate("wmctrl", "-a", "CehoProxy Light") || Activate("xdotool", "search", "--name", "CehoProxy Light", "windowactivate");
            return false;
        }
        catch { return false; }
    }

    private static bool Activate(string tool, params string[] args)
    {
        if (FromPath(tool) is null) return false;
        var info = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var run = Process.Start(info);
        if (run is null) return false;
        if (!run.WaitForExit(2000)) { try { run.Kill(); } catch { } return false; }
        return run.ExitCode == 0;
    }

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);

    private static string? FromPath(string name)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
