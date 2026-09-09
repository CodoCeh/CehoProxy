using System.Diagnostics;

namespace ProxyCage.Core;

/// <summary>Native file picker for the admin panel (runs on the machine where CehoProxy serves the panel).</summary>
public static class AppPathPicker
{
    public static string? Pick(string lang)
    {
        var title = Strings.T(lang, "app_pick_title");
        return Os.Kind switch
        {
            OsKind.Windows => PickWindows(title),
            OsKind.Mac => PickMac(title),
            _ => PickLinux(title),
        };
    }

    private static string? PickWindows(string title)
    {
        var safeTitle = title.Replace("'", "''");
        var script = $@"
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Application]::EnableVisualStyles()
$d = New-Object System.Windows.Forms.OpenFileDialog
$d.Filter = 'Programs (*.exe)|*.exe|All files (*.*)|*.*'
$d.Title = '{safeTitle}'
$d.Multiselect = $false
if ($d.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {{ exit 2 }}
[Console]::Out.Write($d.FileName)
";
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -STA -ExecutionPolicy Bypass -Command {QuotePs(script)}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        return RunPicker(psi);
    }

    private static string? PickMac(string title)
    {
        var safeTitle = title.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var script =
            $"POSIX path of (choose file with prompt \"{safeTitle}\" " +
            "of type {\"com.apple.application-bundle\", \"public.unix-executable\"} " +
            "default location alias \"Applications:\" without invisibles)";
        var psi = new ProcessStartInfo
        {
            FileName = "/usr/bin/osascript",
            Arguments = $"-e {QuoteSh(script)}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        return RunPicker(psi);
    }

    private static string? PickLinux(string title)
    {
        if (Which("zenity") is null && Which("kdialog") is null)
            return null;

        ProcessStartInfo psi;
        if (Which("zenity") is not null)
        {
            psi = new ProcessStartInfo
            {
                FileName = "zenity",
                Arguments = $"--file-selection --title={QuoteSh(title)} " +
                            "--file-filter=Programs | *.exe *.bin * --file-filter=All | *",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
        }
        else
        {
            psi = new ProcessStartInfo
            {
                FileName = "kdialog",
                Arguments = $"--getopenfilename ~ {QuoteSh(title)}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
        }

        return RunPicker(psi);
    }

    private static string? RunPicker(ProcessStartInfo psi)
    {
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(120_000);
            if (p.ExitCode == 2) return null; // cancelled
            if (p.ExitCode != 0 || output.Length == 0) return null;
            return output;
        }
        catch
        {
            return null;
        }
    }

    private static string? Which(string name)
    {
        if (Os.IsWindows)
        {
            var (code, output) = Os.Run("where.exe", name, 5000);
            if (code != 0) return null;
            var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            return line is { Length: > 0 } ? line : null;
        }

        var (c, o) = Os.Run("which", name, 5000);
        return c == 0 && o.Trim().Length > 0 ? o.Trim() : null;
    }

    private static string QuotePs(string script) =>
        "'" + script.Replace("'", "''") + "'";

    private static string QuoteSh(string text) =>
        "'" + text.Replace("'", "'\\''") + "'";
}
