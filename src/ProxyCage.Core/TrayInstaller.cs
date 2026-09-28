using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace ProxyCage.Core;

public static class TrayInstaller
{
    public const string LinuxFileName = "cehoproxy-tray";
    public const string LinuxAutostartPath = "/etc/xdg/autostart/cehoproxy-tray.desktop";

    public static string AssetName(OsKind kind, Architecture arch)
    {
        var cpu = arch == Architecture.Arm64 ? "arm64" : "x64";
        return kind switch
        {
            OsKind.Windows => "cehoproxy-tray-win-x64.exe",
            OsKind.Mac => $"cehoproxy-tray-osx-{cpu}.zip",
            _ => $"cehoproxy-tray-linux-{cpu}",
        };
    }

    public static string DownloadUrl(string repo, string version, string asset) =>
        $"https://github.com/{repo}/releases/download/v{version}/{asset}";

    public static string TrayPath(string root) => Os.Kind switch
    {
        OsKind.Windows => Path.Combine(root, Installer.TrayWindowsFileName),
        OsKind.Mac => Installer.TrayAppPath,
        _ => Path.Combine(root, LinuxFileName),
    };

    public static string? PlistVersion(string plist)
    {
        var match = Regex.Match(plist, @"<key>CFBundleShortVersionString</key>\s*<string>([^<]+)</string>");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    public static string? PlainVersion(string? text)
    {
        var first = text?.Trim().Split('\n', 2)[0].Trim();
        if (string.IsNullOrEmpty(first)) return null;
        var clean = first.Split('+')[0];
        var parts = clean.Split('.');
        return parts.Length >= 3 ? string.Join('.', parts.Take(3)) : clean;
    }

    public static string? InstalledVersion(string root)
    {
        var path = TrayPath(root);
        try
        {
            switch (Os.Kind)
            {
                case OsKind.Windows:
                    return File.Exists(path) ? PlainVersion(FileVersionInfo.GetVersionInfo(path).ProductVersion) : null;
                case OsKind.Mac:
                    var plist = Path.Combine(path, "Contents", "Info.plist");
                    return File.Exists(plist) ? PlistVersion(File.ReadAllText(plist)) : null;
                default:
                    if (!File.Exists(path)) return null;
                    var (code, output) = Os.Run(path, "--version", 15000);
                    return code == 0 ? PlainVersion(output) : "";
            }
        }
        catch { return ""; }
    }

    public static bool GnomeWithoutIndicators(string extensions = "/usr/share/gnome-shell/extensions") =>
        File.Exists("/usr/bin/gnome-shell")
        && !new[] { "appindicatorsupport@rgcjonas.gmail.com", "ubuntu-appindicators@ubuntu.com" }
            .Any(name => Directory.Exists(Path.Combine(extensions, name)));

    public static bool HasDesktop() =>
        new[] { "/usr/share/xsessions", "/usr/share/wayland-sessions" }
            .Any(dir => Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.desktop").Any());

    public static async Task EnsureAsync(
        string root, string repo, bool installIfMissing, Action<string> log, string lang = "ru")
    {
        if (!Os.IsElevated()) return;
        var version = Updater.CurrentVersion;
        var installed = InstalledVersion(root);
        if (installed == version) return;
        if (installed is null && (!installIfMissing || Os.IsWindows || (Os.IsLinux && !HasDesktop()))) return;

        var asset = AssetName(Os.Kind, RuntimeInformation.OSArchitecture);
        var download = Path.Combine(root, asset + ".download");
        try
        {
            using (var http = DirectHttp.CreateClient(TimeSpan.FromMinutes(5)))
            await using (var stream = await http.GetStreamAsync(DownloadUrl(repo, version, asset)))
            await using (var file = File.Create(download))
                await stream.CopyToAsync(file);

            switch (Os.Kind)
            {
                case OsKind.Windows: PlaceWindows(root, download); break;
                case OsKind.Mac: PlaceMac(download); break;
                default: PlaceLinux(root, download); break;
            }
            log(Strings.T(lang, installed is null ? "tray_installed" : "tray_updated", version));
            if (Os.IsLinux && GnomeWithoutIndicators()) log(Strings.T(lang, "tray_gnome_hint"));
        }
        catch (Exception ex)
        {
            log(Strings.T(lang, "tray_update_failed", ex.Message));
        }
        finally
        {
            try { File.Delete(download); } catch { }
        }
    }

    private static void PlaceWindows(string root, string download)
    {
        var target = TrayPath(root);
        var old = target + ".old";
        try { File.Delete(old); } catch { }
        if (File.Exists(target)) File.Move(target, old, overwrite: true);
        File.Move(download, target);
    }

    private static void PlaceLinux(string root, string download)
    {
        var target = TrayPath(root);
        File.SetUnixFileMode(download,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        File.Move(download, target, overwrite: true);

        Directory.CreateDirectory(Path.GetDirectoryName(LinuxAutostartPath)!);
        File.WriteAllText(LinuxAutostartPath, LinuxAutostart(target));
        StartForDesktopUser(target);
    }

    public const string WindowsSessionTask = "CehoProxyTray";

    public static string WindowsSessionTaskXml(string exe) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo><Description>CehoProxy</Description></RegistrationInfo>
          <Principals>
            <Principal id="Users">
              <GroupId>S-1-5-32-545</GroupId>
              <RunLevel>LeastPrivilege</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <Priority>5</Priority>
          </Settings>
          <Actions Context="Users">
            <Exec><Command>{System.Security.SecurityElement.Escape(exe)}</Command></Exec>
          </Actions>
        </Task>
        """;

    public static bool StartInWindowsSessions(string root)
    {
        var exe = TrayPath(root);
        if (!Os.IsWindows || !File.Exists(exe)) return false;
        var xml = Path.Combine(root, WindowsSessionTask + ".xml");
        try
        {
            File.WriteAllText(xml, WindowsSessionTaskXml(exe), System.Text.Encoding.Unicode);
            return Os.Run("schtasks", $"/Create /TN {WindowsSessionTask} /XML \"{xml}\" /F").Code == 0
                   && Os.Run("schtasks", $"/Run /TN {WindowsSessionTask}").Code == 0;
        }
        finally
        {
            try { File.Delete(xml); } catch { }
        }
    }

    public static string LinuxAutostart(string exe) => $"""
        [Desktop Entry]
        Type=Application
        Name=CehoProxy
        Exec="{exe}"
        NoDisplay=true
        X-GNOME-Autostart-enabled=true
        """ + "\n";

    private static void StartForDesktopUser(string exe)
    {
        var user = Environment.GetEnvironmentVariable("SUDO_USER");
        if (string.IsNullOrWhiteSpace(user) || user == "root") return;
        var uid = Os.Run("id", $"-u {user}").Output.Trim();
        if (uid.Length == 0 || !File.Exists($"/run/user/{uid}/bus")) return;
        Os.Run("runuser", $"-u {user} -- env XDG_RUNTIME_DIR=/run/user/{uid} " +
                          $"DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/{uid}/bus " +
                          $"systemd-run --user --collect --unit=cehoproxy-tray-{DateTime.UtcNow:HHmmss} \"{exe}\"");
    }

    private static void PlaceMac(string download)
    {
        var unpacked = download + ".dir";
        try { if (Directory.Exists(unpacked)) Directory.Delete(unpacked, true); } catch { }
        Directory.CreateDirectory(unpacked);
        var (code, output) = Os.Run("ditto", $"-x -k \"{download}\" \"{unpacked}\"");
        var app = Path.Combine(unpacked, Path.GetFileName(Installer.TrayAppPath));
        if (code != 0 || !Directory.Exists(app)) throw new InvalidOperationException(output.Trim());

        var user = Os.Run("stat", "-f %Su /dev/console").Output.Trim();
        var uid = user is { Length: > 0 } and not "root" ? Os.Run("id", $"-u {user}").Output.Trim() : "";
        if (uid.Length > 0) Os.Run("launchctl", $"bootout gui/{uid}/{Installer.TrayLaunchAgentLabel}");
        Os.Run("pkill", "-f CehoProxyTray");

        if (Directory.Exists(Installer.TrayAppPath)) Directory.Delete(Installer.TrayAppPath, true);
        Directory.Move(app, Installer.TrayAppPath);
        try { Directory.Delete(unpacked, true); } catch { }
        Os.Run("xattr", $"-dr com.apple.quarantine \"{Installer.TrayAppPath}\"");
        if (uid.Length == 0) return;

        Os.Run("chown", $"-R {user} \"{Installer.TrayAppPath}\"");
        var home = Os.Run("dscl", $". -read /Users/{user} NFSHomeDirectory").Output
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
        if (string.IsNullOrEmpty(home)) return;
        var agents = Path.Combine(home, "Library", "LaunchAgents");
        Directory.CreateDirectory(agents);
        var plist = Path.Combine(agents, Installer.TrayLaunchAgentLabel + ".plist");
        File.WriteAllText(plist, MacLaunchAgent());
        Os.Run("chown", $"{user} \"{plist}\"");
        Os.Run("launchctl", $"bootstrap gui/{uid} \"{plist}\"");
        Os.Run("launchctl", $"kickstart -k gui/{uid}/{Installer.TrayLaunchAgentLabel}");
    }

    public static string MacLaunchAgent() => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>Label</key><string>{Installer.TrayLaunchAgentLabel}</string>
          <key>ProgramArguments</key>
          <array><string>{Installer.TrayAppPath}/Contents/MacOS/CehoProxyTray</string></array>
          <key>RunAtLoad</key><true/>
          <key>KeepAlive</key><false/>
          <key>LimitLoadToSessionType</key><string>Aqua</string>
        </dict>
        </plist>
        """ + "\n";
}
