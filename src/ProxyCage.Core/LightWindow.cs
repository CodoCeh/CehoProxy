using System.Diagnostics;

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

    public static bool Open(string root, string url)
    {
        var exe = Find(Os.Kind, Environment.GetEnvironmentVariable, FromPath, File.Exists);
        if (exe is null)
        {
            Os.OpenInBrowser(url);
            return false;
        }
        try
        {
            var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in Arguments(url, Path.Combine(root, "light-profile"))) info.ArgumentList.Add(arg);
            Process.Start(info);
            return true;
        }
        catch
        {
            Os.OpenInBrowser(url);
            return false;
        }
    }

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
