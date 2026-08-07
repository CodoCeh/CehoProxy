namespace ProxyCage.Core;

/// <summary>
/// Автозапуск средствами самой системы: планировщик задач, systemd или launchd.
/// Везде нужны права администратора — TUN без них не поднимется, и запускать
/// защиту «под пользователем» бессмысленно.
/// </summary>
public static class Autostart
{
    public const string TaskName = "CehoProxy";
    public const string ServiceName = "cehoproxy";
    public const string LaunchdLabel = "ru.codoceh.cehoproxy";

    private const string UnitPath = "/etc/systemd/system/cehoproxy.service";
    private const string PlistPath = "/Library/LaunchDaemons/ru.codoceh.cehoproxy.plist";

    public static bool IsEnabled() => Os.Kind switch
    {
        OsKind.Windows => Os.Run("schtasks", $"/query /tn {TaskName}").Code == 0,
        OsKind.Linux => Os.Run("systemctl", $"is-enabled {ServiceName}").Code == 0,
        _ => File.Exists(PlistPath),
    };

    public static string? Enable(string exePath, string workingDir) => Os.Kind switch
    {
        OsKind.Windows => EnableWindows(exePath, workingDir),
        OsKind.Linux => EnableSystemd(exePath, workingDir),
        _ => EnableLaunchd(exePath, workingDir),
    };

    public static string? Disable() => Os.Kind switch
    {
        OsKind.Windows => Os.Run("schtasks", $"/delete /tn {TaskName} /f") is { Code: 0 }
            ? null : "не удалось снять автозапуск",
        OsKind.Linux => DisableSystemd(),
        _ => DisableLaunchd(),
    };

    /// <summary>Перезапуск службы после обновления файла программы.</summary>
    public static void Restart()
    {
        switch (Os.Kind)
        {
            case OsKind.Windows:
                Os.Run("schtasks", $"/end /tn {TaskName}");
                Os.Run("schtasks", $"/run /tn {TaskName}");
                break;
            case OsKind.Linux:
                Os.Run("systemctl", $"restart {ServiceName}");
                break;
            default:
                Os.Run("launchctl", $"kickstart -k system/{LaunchdLabel}");
                break;
        }
    }

    /// <summary>Останавливает службу прямо сейчас. Disable только снимает её с автозапуска.</summary>
    public static void StopService()
    {
        switch (Os.Kind)
        {
            case OsKind.Windows: Os.Run("schtasks", $"/end /tn {TaskName}"); break;
            case OsKind.Linux: Os.Run("systemctl", $"stop {ServiceName}"); break;
            default: Os.Run("launchctl", $"bootout system/{LaunchdLabel}"); break;
        }
    }

    /// <summary>Полное удаление: остановить, снять автозапуск и стереть его файлы из системы.</summary>
    public static void Purge()
    {
        // сначала остановить: снятая с автозапуска служба продолжает работать,
        // и после удаления на машине оставался живой туннель с открытой панелью
        StopService();
        Disable();
        try
        {
            if (Os.IsLinux && File.Exists(UnitPath))
            {
                File.Delete(UnitPath);
                Os.Run("systemctl", "daemon-reload");
            }
            if (Os.IsMac && File.Exists(PlistPath)) File.Delete(PlistPath);
        }
        catch { }
    }

    // ── Windows ───────────────────────────────────────────────────────

    private static string? EnableWindows(string exePath, string workingDir)
    {
        // S4U — задача стартует без интерактивного входа и без хранения пароля,
        // Highest — TUN требует прав администратора.
        var ps = $$"""
            $ErrorActionPreference='Stop'
            Unregister-ScheduledTask -TaskName '{{TaskName}}' -Confirm:$false -ErrorAction SilentlyContinue
            $a=New-ScheduledTaskAction -Execute '{{exePath}}' -Argument 'daemon' -WorkingDirectory '{{workingDir}}'
            $t=New-ScheduledTaskTrigger -AtLogOn
            $p=New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType S4U -RunLevel Highest
            $s=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
            Register-ScheduledTask -TaskName '{{TaskName}}' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null
            """;
        var (code, output) = Os.Run("powershell",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"{ps.Replace("\"", "\\\"").ReplaceLineEndings("; ")}\"");
        return code == 0 ? null : $"не удалось включить автозапуск: {output}";
    }

    // ── Linux ─────────────────────────────────────────────────────────

    private static string? EnableSystemd(string exePath, string workingDir)
    {
        if (Os.FindOnPath("systemctl") is null)
            return "В системе нет systemd. Пропиши запуск «cehoproxy daemon» тем механизмом, " +
                   "который используется у тебя (например, добавь в /etc/rc.local).";

        var unit = $"""
            [Unit]
            Description=CehoProxy — изоляция приложений в туннель
            Documentation=https://codoceh.ru
            After=network-online.target
            Wants=network-online.target

            [Service]
            Type=simple
            ExecStart={exePath} daemon
            WorkingDirectory={workingDir}
            Restart=on-failure
            RestartSec=5
            # движку нужны NET_ADMIN для TUN и чтение /proc для правил по приложениям
            AmbientCapabilities=CAP_NET_ADMIN CAP_NET_RAW
            KillSignal=SIGTERM
            TimeoutStopSec=20

            [Install]
            WantedBy=multi-user.target
            """;

        try { File.WriteAllText(UnitPath, unit); }
        catch (Exception ex) { return $"не удалось записать {UnitPath}: {ex.Message}"; }

        var (reloadCode, reloadOut) = Os.Run("systemctl", "daemon-reload");
        if (reloadCode != 0) return $"systemd не перечитал юнит: {reloadOut}";

        var (code, output) = Os.Run("systemctl", $"enable {ServiceName}");
        return code == 0 ? null : $"не удалось включить автозапуск: {output}";
    }

    private static string? DisableSystemd()
    {
        if (!File.Exists(UnitPath)) return null;
        var (code, output) = Os.Run("systemctl", $"disable {ServiceName}");
        return code == 0 ? null : $"не удалось снять автозапуск: {output}";
    }

    // ── macOS ─────────────────────────────────────────────────────────

    private static string? EnableLaunchd(string exePath, string workingDir)
    {
        var plist = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key><string>{LaunchdLabel}</string>
              <key>ProgramArguments</key>
              <array>
                <string>{exePath}</string>
                <string>daemon</string>
              </array>
              <key>RunAtLoad</key><true/>
              <key>KeepAlive</key>
              <dict><key>SuccessfulExit</key><false/></dict>
              <key>WorkingDirectory</key><string>{workingDir}</string>
              <key>StandardErrorPath</key><string>{Path.Combine(workingDir, "cehoproxy.log")}</string>
            </dict>
            </plist>
            """;

        try
        {
            File.WriteAllText(PlistPath, plist);
            // launchd молча игнорирует plist с неправильными правами
            Os.Run("chown", $"root:wheel {PlistPath}");
            Os.Run("chmod", $"644 {PlistPath}");
        }
        catch (Exception ex) { return $"не удалось записать {PlistPath}: {ex.Message}"; }

        Os.Run("launchctl", $"bootout system/{LaunchdLabel}");
        var (code, output) = Os.Run("launchctl", $"bootstrap system {PlistPath}");
        return code == 0 ? null : $"не удалось включить автозапуск: {output}";
    }

    private static string? DisableLaunchd()
    {
        Os.Run("launchctl", $"bootout system/{LaunchdLabel}");
        try { if (File.Exists(PlistPath)) File.Delete(PlistPath); }
        catch (Exception ex) { return $"не удалось убрать {PlistPath}: {ex.Message}"; }
        return null;
    }
}
