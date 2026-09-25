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

    public static string WindowsUpdateRelaunchScript(
        int pid, string exe, string downloaded, string root, bool autostart,
        string expectedVersion, string jobId, string statusPath)
    {
        static string Q(string s) => s.Replace("'", "''");
        var start = autostart
            ? "schtasks /run /tn CehoProxy | Out-Null; if ($LASTEXITCODE -ne 0) { throw 'Не удалось запустить задачу CehoProxy' }"
            : $"Start-Process -FilePath '{Q(exe)}' -ArgumentList 'daemon' -WorkingDirectory '{Q(root)}' -WindowStyle Hidden";

        return $$"""
            $ErrorActionPreference = 'Stop'
            $watch = {{pid}}
            $exe = '{{Q(exe)}}'
            $downloaded = '{{Q(downloaded)}}'
            $backup = '{{Q(exe)}}.old'
            $root = '{{Q(root)}}'
            $statusPath = '{{Q(statusPath)}}'
            $expectedVersion = '{{Q(expectedVersion)}}'
            $jobId = '{{Q(jobId)}}'
            $logPath = Join-Path $root 'cehoproxy-update.log'
            $previousVersion = ''
            $movedCurrentToBackup = $false
            $exitCode = 0

            function Set-UpdateStatus([string]$state, [string]$message) {
              $payload = @{ jobId = $jobId; state = $state; version = $expectedVersion; message = $message } | ConvertTo-Json -Compress
              $temporary = $statusPath + '.tmp'
              [System.IO.File]::WriteAllText($temporary, $payload, [System.Text.UTF8Encoding]::new($false))
              Move-Item -LiteralPath $temporary -Destination $statusPath -Force
            }

            function Wait-InstalledDaemon {
              $deadline = (Get-Date).AddSeconds(45)
              while ((Get-Date) -lt $deadline) {
                if (Test-Path -LiteralPath (Join-Path $root 'cehoproxy.pid')) {
                  $daemonPid = 0
                  $pidText = (Get-Content -LiteralPath (Join-Path $root 'cehoproxy.pid') -Raw).Trim()
                  if ([int]::TryParse($pidText, [ref]$daemonPid)) {
                    $daemon = Get-Process -Id $daemonPid -ErrorAction SilentlyContinue
                    if ($daemon -and $daemon.Path -and ([System.IO.Path]::GetFullPath($daemon.Path) -ieq [System.IO.Path]::GetFullPath($exe))) { return $true }
                  }
                }
                Start-Sleep -Seconds 1
              }
              return $false
            }

            function Stop-InstalledDaemon {
              if ({{(autostart ? "$true" : "$false")}}) { schtasks /end /tn CehoProxy | Out-Null }
              $pidFile = Join-Path $root 'cehoproxy.pid'
              if (Test-Path -LiteralPath $pidFile) {
                $daemonPid = 0
                $pidText = (Get-Content -LiteralPath $pidFile -Raw).Trim()
                if ([int]::TryParse($pidText, [ref]$daemonPid)) {
                  $daemon = Get-Process -Id $daemonPid -ErrorAction SilentlyContinue
                  if ($daemon -and $daemon.Path -and ([System.IO.Path]::GetFullPath($daemon.Path) -ieq [System.IO.Path]::GetFullPath($exe))) {
                    Stop-Process -Id $daemonPid -Force -ErrorAction SilentlyContinue
                    Wait-Process -Id $daemonPid -Timeout 10 -ErrorAction SilentlyContinue
                  }
                }
              }
            }

            try {
              while (Get-Process -Id $watch -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 1 }
              Start-Sleep -Milliseconds 500
              if (-not (Test-Path -LiteralPath $downloaded)) { throw 'Загруженный файл обновления не найден' }
              if (Test-Path -LiteralPath $exe) {
                $oldVersionOutput = & $exe version 2>&1
                if ($LASTEXITCODE -eq 0) { $previousVersion = ([string]$oldVersionOutput[0]).Trim() }
              }
              if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Force }
              if (Test-Path -LiteralPath $exe) {
                Move-Item -LiteralPath $exe -Destination $backup -Force
                $movedCurrentToBackup = $true
              }
              Move-Item -LiteralPath $downloaded -Destination $exe -Force

              $versionOutput = & $exe version 2>&1
              if ($LASTEXITCODE -ne 0 -or ([string]$versionOutput[0]).Trim() -ne $expectedVersion) {
                throw "Проверка версии не прошла: ожидалась $expectedVersion"
              }

              {{start}}
              if (-not (Wait-InstalledDaemon)) { throw 'Новый daemon не запустился' }
              try { Add-Content -LiteralPath $logPath -Value "[$(Get-Date -Format o)] verified version $expectedVersion" } catch {}
              Set-UpdateStatus 'verified' "Установка версии $expectedVersion проверена."
              try { if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Force } } catch {}
              Write-Host "CehoProxy update verified: $expectedVersion"
            }
            catch {
              $failure = $_.Exception.Message
              $rollbackMessage = 'Не удалось проверить обновление. Откат предыдущей версии не подтверждён; подробности в журнале обновления.'
              try {
                Stop-InstalledDaemon
                if ($movedCurrentToBackup -and (Test-Path -LiteralPath $backup)) {
                  if (Test-Path -LiteralPath $exe) { Remove-Item -LiteralPath $exe -Force }
                  Move-Item -LiteralPath $backup -Destination $exe -Force
                }
                if (Test-Path -LiteralPath $exe) {
                  if ($previousVersion) {
                    $rollbackVersionOutput = & $exe version 2>&1
                    if ($LASTEXITCODE -ne 0 -or ([string]$rollbackVersionOutput[0]).Trim() -ne $previousVersion) { throw 'Предыдущая версия не восстановилась' }
                  }
                  {{start}}
                  if (-not (Wait-InstalledDaemon)) { throw 'Не удалось поднять предыдущий daemon' }
                  $rollbackMessage = 'Не удалось проверить обновление; предыдущая версия восстановлена.'
                }
              } catch { $failure = $failure + '; rollback/restart: ' + $_.Exception.Message }
              try { Set-UpdateStatus 'failed' $rollbackMessage } catch {}
              try { Add-Content -LiteralPath $logPath -Value "[$(Get-Date -Format o)] update failed: $failure" } catch {}
              Write-Error "CehoProxy update failed: $failure"
              $exitCode = 1
            }
            finally {
              Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
            }
            exit $exitCode
            """;
    }

    public static void SpawnUpdateRelaunchHelper(
        string exe, string downloaded, string root, string expectedVersion, string jobId,
        bool inheritConsole = false)
    {
        var pid = Environment.ProcessId;
        var backup = exe + ".old";
        var autostart = Autostart.IsEnabled();

        if (Os.IsWindows)
        {
            var script = Path.Combine(root, "update-relaunch.ps1");
            File.WriteAllText(script, WindowsUpdateRelaunchScript(
                pid, exe, downloaded, root, autostart, expectedVersion, jobId,
                UpdateHandoff.PathFor(root)));
            using var helper = Process.Start(new ProcessStartInfo("powershell", $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"")
            {
                UseShellExecute = false, CreateNoWindow = !inheritConsole, WorkingDirectory = root,
            }) ?? throw new InvalidOperationException("Не удалось запустить помощник обновления Windows.");
            return;
        }

        var sh = Path.Combine(root, "update-relaunch.sh");
        var startUnix = autostart
            ? (Os.IsLinux ? "systemctl start cehoproxy" : "launchctl kickstart -k system/ru.codoceh.cehoproxy")
            : $"nohup \"{exe}\" daemon >/dev/null 2>&1 &";
        var launchdLabel = $"ru.codoceh.cehoproxy.update.{pid}";
        var cleanupLaunchd = Os.IsMac && autostart ? $"launchctl remove {launchdLabel}" : ":";
        File.WriteAllText(sh, $$"""
            #!/bin/sh
            while kill -0 {{pid}} 2>/dev/null; do sleep 1; done
            sleep 1
            rm -f "{{backup}}"
            if [ -f "{{exe}}" ]; then mv "{{exe}}" "{{backup}}"; fi
            if ! mv "{{downloaded}}" "{{exe}}"; then
              [ -f "{{exe}}" ] || mv "{{backup}}" "{{exe}}"
              {{startUnix}}
              rm -f "{{sh}}"
              {{cleanupLaunchd}}
              exit 0
            fi
            chmod 755 "{{exe}}"
            {{startUnix}}
            rm -f "{{sh}}"
            {{cleanupLaunchd}}
            """);
        if (Os.IsLinux && autostart)
        {
            // systemd завершает дочерние процессы службы вместе с daemon.
            // Отдельный transient unit переживёт выход старой версии.
            using var helper = Process.Start(CreateSystemdUpdateStartInfo(sh, root, pid))
                ?? throw new InvalidOperationException("не удалось запустить помощник обновления");
            if (!helper.WaitForExit(10000) || helper.ExitCode != 0)
                throw new InvalidOperationException("systemd не запустил помощник обновления");
        }
        else if (Os.IsMac && autostart)
        {
            using var helper = Process.Start(CreateLaunchdUpdateStartInfo(sh, root, launchdLabel))
                ?? throw new InvalidOperationException("не удалось запустить помощник обновления");
            if (!helper.WaitForExit(10000) || helper.ExitCode != 0)
                throw new InvalidOperationException("launchd не запустил помощник обновления");
        }
        else
        {
            Process.Start(CreateUnixHelperStartInfo(sh, root));
        }
    }

    internal static ProcessStartInfo CreateSystemdUpdateStartInfo(string helperPath, string workingDirectory, int pid)
    {
        var startInfo = new ProcessStartInfo("systemd-run")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };
        startInfo.ArgumentList.Add($"--unit=cehoproxy-update-{pid}");
        startInfo.ArgumentList.Add("--collect");
        startInfo.ArgumentList.Add("/bin/sh");
        startInfo.ArgumentList.Add(helperPath);
        return startInfo;
    }

    internal static ProcessStartInfo CreateLaunchdUpdateStartInfo(string helperPath, string workingDirectory, string label)
    {
        var startInfo = new ProcessStartInfo("launchctl")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (var arg in new[] { "submit", "-l", label, "-p", "/bin/sh", "--", "/bin/sh", helperPath })
            startInfo.ArgumentList.Add(arg);
        return startInfo;
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
