using System.Diagnostics;
using System.Text;

namespace ProxyCage.Core;

/// <summary>Native file picker for the admin panel (runs on the machine where CehoProxy serves the panel).</summary>
public sealed record AppPickResult(string? Path, string? ErrorKey);

public static class AppPathPicker
{
    private const string TaskName = "CehoProxyPickApp";
    private const string ScriptName = "pick-app.ps1";
    private const string LauncherName = "pick-app-launch.vbs";
    private const string ResultName = "pick-app-result.txt";

    public static AppPickResult Pick(string lang, string? root = null, int panelPort = 0)
    {
        root ??= Os.DefaultRoot;
        var title = Strings.T(lang, "app_pick_title");
        return Os.Kind switch
        {
            OsKind.Windows => PickWindows(title, root, panelPort),
            OsKind.Mac => PickMac(title),
            _ => PickLinux(title),
        };
    }

    private static AppPickResult PickWindows(string title, string root, int panelPort)
    {
        if (Process.GetCurrentProcess().SessionId > 0)
        {
            var direct = PickWindowsDirect(title);
            if (direct.Path is not null || direct.ErrorKey is null)
                return direct;
        }

        return PickWindowsInteractive(title, root, panelPort);
    }

    private static AppPickResult PickWindowsDirect(string title)
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
            Arguments = $"-NoProfile -WindowStyle Hidden -STA -ExecutionPolicy Bypass -Command {QuotePs(script)}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        return RunDirectPicker(psi);
    }

    private static AppPickResult PickWindowsInteractive(string title, string root, int panelPort)
    {
        try
        {
            Directory.CreateDirectory(root);
        }
        catch
        {
            return new(null, "app_pick_failed");
        }

        var user = GetInteractiveWindowsUser(panelPort);
        if (user is null)
            return new(null, "app_pick_no_user");

        var scriptPath = Path.Combine(root, ScriptName);
        var launcherPath = Path.Combine(root, LauncherName);
        var resultPath = Path.Combine(root, ResultName);

        try
        {
            WritePickScript(scriptPath, resultPath, title);
            WriteLauncher(launcherPath, scriptPath, resultPath, title);
            TryDelete(resultPath);
        }
        catch
        {
            return new(null, "app_pick_failed");
        }

        if (!RunInteractiveTask(user, launcherPath))
            return new(null, "app_pick_failed");

        var line = WaitForResult(resultPath, 120_000);
        CleanupTask();

        if (line is null)
            return new(null, "app_pick_failed");
        if (line == "CANCEL" || line.Length == 0)
            return new(null, null);
        if (line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            return new(null, "app_pick_failed");

        return new(line, null);
    }

    private static void WritePickScript(string scriptPath, string resultPath, string title)
    {
        var safeTitle = title.Replace("'", "''");
        var safeResult = resultPath.Replace("'", "''");
        var script = $@"param(
  [string]$Title = '{safeTitle}',
  [string]$OutFile = '{safeResult}'
)
try {{
  Add-Type -AssemblyName System.Windows.Forms
  [System.Windows.Forms.Application]::EnableVisualStyles()
  $d = New-Object System.Windows.Forms.OpenFileDialog
  $d.Filter = 'Programs (*.exe)|*.exe|All files (*.*)|*.*'
  $d.Title = $Title
  $d.Multiselect = $false
  if ($d.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {{
    Set-Content -Path $OutFile -Value 'CANCEL' -Encoding UTF8 -NoNewline
    exit 0
  }}
  Set-Content -Path $OutFile -Value $d.FileName -Encoding UTF8 -NoNewline
}} catch {{
  Set-Content -Path $OutFile -Value ('ERROR:' + $_.Exception.Message) -Encoding UTF8 -NoNewline
  exit 1
}}
";
        File.WriteAllText(scriptPath, script, Encoding.UTF8);
    }

    private static void WriteLauncher(string launcherPath, string scriptPath, string resultPath, string title)
    {
        var safeTitle = title.Replace("\"", "\"\"");
        var ps =
            "powershell.exe -NoProfile -WindowStyle Hidden -STA -ExecutionPolicy Bypass -File \"" +
            scriptPath + "\" -Title \"" + safeTitle + "\" -OutFile \"" + resultPath + "\"";
        var vbs = "CreateObject(\"Wscript.Shell\").Run \"" + ps.Replace("\"", "\"\"") + "\", 0, False\r\n";
        File.WriteAllText(launcherPath, vbs, Encoding.ASCII);
    }

    private static bool RunInteractiveTask(string user, string launcherPath)
    {
        Os.Run("schtasks.exe", $"/Delete /TN {TaskName} /F", 10_000);
        var ru = EscapeSchtasksUser(user);
        var tr = $"wscript.exe //B //Nologo \"{launcherPath}\"";
        var createArgs =
            $"/Create /TN {TaskName} /TR \"{tr}\" /SC ONCE /ST 23:59 /RU {ru} /IT /F";
        var (createCode, createOut) = Os.Run("schtasks.exe", createArgs, 15_000);
        if (createCode != 0)
        {
            var shortUser = user.Contains('\\') ? user[(user.LastIndexOf('\\') + 1)..] : user;
            if (!string.Equals(shortUser, user, StringComparison.OrdinalIgnoreCase))
            {
                createArgs =
                    $"/Create /TN {TaskName} /TR \"{tr}\" /SC ONCE /ST 23:59 /RU {shortUser} /IT /F";
                (createCode, createOut) = Os.Run("schtasks.exe", createArgs, 15_000);
            }
        }

        if (createCode != 0)
            return false;

        var (runCode, _) = Os.Run("schtasks.exe", $"/Run /TN {TaskName}", 15_000);
        return runCode == 0;
    }

    private static void CleanupTask() =>
        Os.Run("schtasks.exe", $"/Delete /TN {TaskName} /F", 10_000);

    private static string? WaitForResult(string resultPath, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (File.Exists(resultPath))
            {
                try
                {
                    var text = File.ReadAllText(resultPath).Trim();
                    TryDelete(resultPath);
                    return text;
                }
                catch
                {
                    Thread.Sleep(100);
                    continue;
                }
            }

            Thread.Sleep(200);
        }

        TryDelete(resultPath);
        return null;
    }

    /// <summary>
    /// На RDS <c>Win32_ComputerSystem.UserName</c> пустой: консоли нет, люди сидят в RDP.
    /// Берём владельца explorer, кто открыл панель, или пользователя службы, если это не SYSTEM.
    /// </summary>
    internal static string? ChooseInteractiveUser(
        string? computerSystemUser,
        string? environmentUser,
        IReadOnlyList<string> explorerOwners,
        IReadOnlyList<string> panelClientOwners)
    {
        foreach (var u in panelClientOwners)
            if (!AppIsolation.IsServiceAccount(u)) return u;

        if (!AppIsolation.IsServiceAccount(computerSystemUser))
            return computerSystemUser!.Trim();

        var humans = explorerOwners
            .Where(u => !AppIsolation.IsServiceAccount(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!AppIsolation.IsServiceAccount(environmentUser))
        {
            var match = humans.FirstOrDefault(h => AppIsolation.SameWindowsUser(h, environmentUser));
            if (match is not null) return match;
            return environmentUser!.Trim();
        }

        return humans.Count > 0 ? humans[0] : null;
    }

    private static string? GetInteractiveWindowsUser(int panelPort)
    {
        var computer = QueryComputerSystemUser();
        var explorers = QueryProcessOwners("explorer.exe");
        var panel = panelPort > 0 ? QueryPanelClientOwners(panelPort) : Array.Empty<string>();
        return ChooseInteractiveUser(computer, Environment.UserName, explorers, panel);
    }

    private static string? QueryComputerSystemUser()
    {
        var (code, output) = Os.Run(
            "powershell.exe",
            "-NoProfile -WindowStyle Hidden -Command \"(Get-CimInstance Win32_ComputerSystem).UserName\"",
            10_000);
        if (code != 0) return null;
        var user = output.Trim();
        return user.Length > 0 ? user : null;
    }

    private static IReadOnlyList<string> QueryProcessOwners(string exeName)
    {
        var list = new List<string>();
        foreach (var (_, user, _) in Os.WindowsProcessesOwned(exeName))
            if (!string.IsNullOrWhiteSpace(user)) list.Add(user);
        return list;
    }

    private static IReadOnlyList<string> QueryPanelClientOwners(int port)
    {
        var (code, output) = Os.Run(
            "powershell.exe",
            "-NoProfile -WindowStyle Hidden -Command \"" +
            "Get-NetTCPConnection -LocalPort " + port + " -State Established -ErrorAction SilentlyContinue | " +
            "ForEach-Object { $_.OwningProcess } | Sort-Object -Unique | ForEach-Object { " +
            "$p=Get-CimInstance Win32_Process -Filter (\\\"ProcessId=\"+$_); if(-not $p){return}; " +
            "$u=''; try { $o=Invoke-CimMethod -InputObject $p -MethodName GetOwner; if($o){ $u=[string]$o.User } } catch {}; " +
            "if($u){ Write-Output $u } }\"",
            15_000);
        if (code != 0) return Array.Empty<string>();
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToList();
    }

    private static string EscapeSchtasksUser(string user) =>
        user.Contains(' ') ? $"\"{user}\"" : user;

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }

    private static AppPickResult PickMac(string title)
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
        return RunDirectPicker(psi);
    }

    private static AppPickResult PickLinux(string title)
    {
        if (Which("zenity") is null && Which("kdialog") is null)
            return new(null, "app_pick_failed");

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

        return RunDirectPicker(psi);
    }

    private static AppPickResult RunDirectPicker(ProcessStartInfo psi)
    {
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return new(null, "app_pick_failed");
            var output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(120_000);
            if (p.ExitCode == 2) return new(null, null); // cancelled
            if (p.ExitCode != 0 || output.Length == 0) return new(null, "app_pick_failed");
            return new(output, null);
        }
        catch
        {
            return new(null, "app_pick_failed");
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
