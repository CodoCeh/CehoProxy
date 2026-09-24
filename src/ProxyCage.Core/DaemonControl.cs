using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ProxyCage.Core;

public static class DaemonControl
{
    public const string ForegroundEnv = "CEHOPROXY_FOREGROUND";

    /// <summary>
    /// Команду набрали в терминале — уходим в фон и возвращаем приглашение.
    /// Служба (launchd, systemd, планировщик) терминала не имеет и остаётся
    /// на переднем плане: иначе система решит, что процесс уже завершился.
    /// </summary>
    public static bool WantsBackground(bool inputRedirected, string? foregroundMarker) =>
        foregroundMarker != "1" && !inputRedirected;

    private const string WindowsEventName = @"Global\CehoProxyStop";
    private const int SIGTERM = 15;

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int PosixKill(int pid, int sig);

    private static readonly string[] OurProcessNames = { "cehoproxy", "chp" };

    public static string PidPath(string root) => Path.Combine(root, "cehoproxy.pid");

    public static int? RunningPid(string root)
    {
        try
        {
            var path = PidPath(root);
            if (!File.Exists(path)) return null;
            if (!int.TryParse(File.ReadAllText(path).Trim(), out var pid)) return null;

            using var p = Process.GetProcessById(pid);

            return OurProcessNames.Any(n => p.ProcessName.Equals(n, StringComparison.OrdinalIgnoreCase))
                ? pid : null;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsRunning(string root) => RunningPid(root) is not null;

    [SupportedOSPlatform("windows")]
    public static bool StopInstalledWindowsDaemons(string root)
    {
        var installed = Os.RealPath(Installer.BinaryPath(root));
        foreach (var process in Process.GetProcessesByName("cehoproxy"))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;

                string? path;
                try { path = process.MainModule?.FileName; }
                catch { continue; }
                if (path is null || !Os.RealPath(path).Equals(installed, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    if (!process.HasExited) process.Kill();
                    if (!process.WaitForExit(5000)) return false;
                }
                catch (InvalidOperationException) { }
                catch { return false; }
            }
        }
        return true;
    }

    public static bool IsStarting(string root, TimeSpan? gracePeriod = null)
    {
        try
        {
            if (!File.Exists(PidPath(root))) return false;
            var started = File.GetLastWriteTimeUtc(PidPath(root));
            return DateTime.UtcNow - started < (gracePeriod ?? TimeSpan.FromMinutes(1));
        }
        catch { return false; }
    }

    /// <summary>Поднять демон, если его ещё нет. Нужно после обновления: install.ps1
    /// гасит процесс до вызова «install --no-setup», и без этого панель не возвращается.</summary>
    public static bool TryStart(string exe, string root)
    {
        if (IsRunning(root)) return true;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return false;

        try
        {
            return StartInBackground(exe, root);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Запускает демон отдельным процессом и не держит терминал.
    /// Потомок сам отцепляется от сессии, поэтому закрытие окна его не гасит.
    /// </summary>
    public static bool StartInBackground(string exe, string root)
    {
        if (IsRunning(root)) return true;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return false;

        try
        {
            Directory.CreateDirectory(root);
            var psi = new ProcessStartInfo(exe, "daemon")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = root,
                RedirectStandardInput = true,
            };
            psi.Environment[ForegroundEnv] = "1";
            if (Process.Start(psi) is null) return false;
            return WaitUntilRunning(root, 8000);
        }
        catch
        {
            return false;
        }
    }

    public static bool WaitUntilRunning(string root, int timeoutMs = 8000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (IsRunning(root)) return true;
            Thread.Sleep(250);
        }
        return IsRunning(root);
    }

    public static void MarkRunning(string root)
    {
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(PidPath(root), Environment.ProcessId.ToString());
        }
        catch { }
    }

    public static void ClearRunning(string root)
    {
        try { File.Delete(PidPath(root)); } catch { }
    }

    public static bool WaitForExit(string root, int timeoutMs = 20000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (RunningPid(root) is null) return true;
            Thread.Sleep(400);
        }
        return RunningPid(root) is null;
    }

    /// <summary>
    /// Обновление из панели не должно убивать себя через schtasks /end:
    /// тогда /run уже некому выполнить, и человек остаётся без интерфейса.
    /// Помощник ждёт, пока наш процесс сам выйдет, и только потом поднимает новый.
    /// </summary>
    public static string WindowsRelaunchScript(int pid, string exe, string root, bool autostart)
    {
        static string Q(string s) => s.Replace("'", "''");
        var start = autostart
            ? "schtasks /run /tn CehoProxy | Out-Null"
            : $"Start-Process -FilePath '{Q(exe)}' -ArgumentList 'daemon' -WorkingDirectory '{Q(root)}' -WindowStyle Hidden";
        return $$"""
            $watch = {{pid}}
            while (Get-Process -Id $watch -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 1 }
            Start-Sleep -Milliseconds 500
            {{start}}

            """;
    }

    public static void SpawnRelaunchHelper(string exe, string root)
    {
        var pid = Environment.ProcessId;
        var autostart = Autostart.IsEnabled();

        if (Os.IsWindows)
        {
            var script = Path.Combine(root, "relaunch.ps1");
            File.WriteAllText(script,
                WindowsRelaunchScript(pid, exe, root, autostart)
                + $"Remove-Item -LiteralPath '{script.Replace("'", "''")}' -Force -ErrorAction SilentlyContinue\n");
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = root,
            });
            return;
        }

        var sh = Path.Combine(root, "relaunch.sh");
        var start = autostart
            ? (Os.IsLinux
                ? "systemctl start cehoproxy"
                : "launchctl kickstart -k system/ru.codoceh.cehoproxy")
            : $"nohup \"{exe}\" daemon >/dev/null 2>&1 &";
        File.WriteAllText(sh, $"""
            #!/bin/sh
            while kill -0 {pid} 2>/dev/null; do sleep 1; done
            sleep 1
            {start}
            rm -f "{sh}"
            """);
        Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/sh",
            Arguments = sh,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = root,
        });
    }

    public static void SpawnUpdateRelaunchHelper(string exe, string downloaded, string root)
    {
        var pid = Environment.ProcessId;
        var backup = exe + ".old";
        var autostart = Autostart.IsEnabled();

        if (Os.IsWindows)
        {
            static string Q(string s) => s.Replace("'", "''");
            var start = autostart
                ? "schtasks /run /tn CehoProxy | Out-Null"
                : $"Start-Process -FilePath '{Q(exe)}' -ArgumentList 'daemon' -WorkingDirectory '{Q(root)}' -WindowStyle Hidden";
            var script = Path.Combine(root, "update-relaunch.ps1");
            File.WriteAllText(script, $$"""
                $watch = {{pid}}
                while (Get-Process -Id $watch -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 1 }
                Start-Sleep -Milliseconds 500
                Remove-Item -LiteralPath '{{Q(backup)}}' -Force -ErrorAction SilentlyContinue
                if (Test-Path -LiteralPath '{{Q(exe)}}') { Move-Item -LiteralPath '{{Q(exe)}}' -Destination '{{Q(backup)}}' -Force }
                try { Move-Item -LiteralPath '{{Q(downloaded)}}' -Destination '{{Q(exe)}}' -Force }
                catch { if (-not (Test-Path -LiteralPath '{{Q(exe)}}') -and (Test-Path -LiteralPath '{{Q(backup)}}')) { Move-Item -LiteralPath '{{Q(backup)}}' -Destination '{{Q(exe)}}' }; throw }
                {{start}}
                Remove-Item -LiteralPath '{{Q(script)}}' -Force -ErrorAction SilentlyContinue
                """);
            Process.Start(new ProcessStartInfo("powershell", $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"")
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root,
            });
            return;
        }

        var sh = Path.Combine(root, "update-relaunch.sh");
        var startUnix = autostart
            ? (Os.IsLinux ? "systemctl start cehoproxy" : "launchctl kickstart -k system/ru.codoceh.cehoproxy")
            : $"nohup \"{exe}\" daemon >/dev/null 2>&1 &";
        File.WriteAllText(sh, $$"""
            #!/bin/sh
            while kill -0 {{pid}} 2>/dev/null; do sleep 1; done
            sleep 1
            rm -f "{{backup}}"
            if [ -f "{{exe}}" ]; then mv "{{exe}}" "{{backup}}"; fi
            if ! mv "{{downloaded}}" "{{exe}}"; then
              [ -f "{{exe}}" ] || mv "{{backup}}" "{{exe}}"
              exit 1
            fi
            chmod 755 "{{exe}}"
            {{startUnix}}
            rm -f "{{sh}}"
            """);
        Process.Start(CreateUnixHelperStartInfo(sh, root));
    }

    internal static ProcessStartInfo CreateUnixHelperStartInfo(string helperPath, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };
        startInfo.ArgumentList.Add(helperPath);
        return startInfo;
    }

    public static bool RequestStop(string root)
    {
        if (OperatingSystem.IsWindows()) return RequestStopWindows();

        var pid = RunningPid(root);
        if (pid is null) return false;
        return PosixKill(pid.Value, SIGTERM) == 0;
    }

    [SupportedOSPlatform("windows")]
    private static bool RequestStopWindows()
    {
        return TrySignalWindowsStopEvent(() =>
        {
            if (!EventWaitHandle.TryOpenExisting(WindowsEventName, out var ev)) return false;
            using (ev) ev.Set();
            return true;
        });
    }

    internal static bool TrySignalWindowsStopEvent(Func<bool> signal)
    {
        try { return signal(); }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static void WaitForStop()
    {
        if (OperatingSystem.IsWindows())
        {
            using var stopEvent = new EventWaitHandle(false, EventResetMode.ManualReset, WindowsEventName);
            stopEvent.Reset();
            stopEvent.WaitOne();
            return;
        }

        using var stop = new ManualResetEventSlim(false);

        using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; stop.Set(); });
        using var intr = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx => { ctx.Cancel = true; stop.Set(); });
        using var hup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, ctx => { ctx.Cancel = true; stop.Set(); });
        stop.Wait();
    }
}
