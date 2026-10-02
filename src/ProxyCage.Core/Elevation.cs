using System.Diagnostics;

namespace ProxyCage.Core;

public enum RightsNeed { None, FolderWrite, Root }

public enum RightsAsk { None, Terminal, Window }

public static class Elevation
{
    public const string HomeEnv = "CEHOPROXY_HOME";

    public static RightsNeed Needed(IReadOnlyList<string> args, string root)
    {
        if (args.Count == 0) return RightsNeed.None;
        return args[0] switch
        {
            "install" or "uninstall" or "uninstal" or "daemon" or "export" or "import" => RightsNeed.Root,
            "autostart" => args.Count >= 2 ? RightsNeed.Root : RightsNeed.None,
            "stop" or "off" => DaemonControl.IsRunning(root) ? RightsNeed.Root : RightsNeed.None,
            "restart" =>
                DaemonControl.IsRunning(root) || Autostart.IsEnabled() ? RightsNeed.Root : RightsNeed.None,
            "update" or "engine" or "движок" => RightsNeed.FolderWrite,
            _ => RightsNeed.None,
        };
    }

    public static bool Satisfied(RightsNeed need, string root) => need switch
    {
        RightsNeed.None => true,
        RightsNeed.Root => Os.IsElevated(),
        _ => Os.IsElevated() || Preflight.FolderIsWritable(root, out _),
    };

    public static RightsAsk Way() => Way(Os.Kind, !Console.IsInputRedirected);

    internal static RightsAsk Way(OsKind kind, bool hasTerminal)
    {
        if (kind == OsKind.Windows) return Os.FindOnPath("powershell") is not null ? RightsAsk.Window : RightsAsk.None;
        if (hasTerminal && Os.FindOnPath("sudo") is not null) return RightsAsk.Terminal;
        if (kind == OsKind.Mac) return RightsAsk.Window;
        return Os.FindOnPath("pkexec") is not null ? RightsAsk.Window : RightsAsk.None;
    }

    public static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    public static IReadOnlyList<string> SudoArguments(string exe, IReadOnlyList<string> args, string? home)
    {
        var list = new List<string> { "--" };
        if (!string.IsNullOrEmpty(home))
        {
            list.Add("env");
            list.Add($"{HomeEnv}={home}");
        }
        list.Add(exe);
        list.AddRange(args);
        return list;
    }

    public static string ShellCommand(string exe, IReadOnlyList<string> args, string? home)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(home)) parts.Add($"{HomeEnv}={ShellQuote(home)}");
        parts.Add(ShellQuote(exe));
        parts.AddRange(args.Select(ShellQuote));
        return string.Join(' ', parts) + " </dev/null >/dev/null 2>&1";
    }

    public static string AppleScript(string exe, IReadOnlyList<string> args, string? home)
    {
        var command = ShellCommand(exe, args, home).Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"do shell script \"{command}\" with administrator privileges";
    }

    public static ProcessStartInfo StartInfo(RightsAsk way, string exe, IReadOnlyList<string> args, string? home)
    {
        if (way == RightsAsk.Terminal)
        {
            var sudo = new ProcessStartInfo("sudo") { UseShellExecute = false };
            foreach (var a in SudoArguments(exe, args, home)) sudo.ArgumentList.Add(a);
            return sudo;
        }

        if (Os.IsWindows)
        {
            static string Q(string v) => v.Replace("'", "''");
            var list = string.Join(",", args.Select(a => $"'{Q(a)}'"));
            var argPart = args.Count == 0 ? "" : $" -ArgumentList {list}";
            var script = $"$p = Start-Process -FilePath '{Q(exe)}'{argPart} -Verb RunAs -Wait -PassThru; exit $p.ExitCode";
            var ps = new ProcessStartInfo("powershell") { UseShellExecute = false, CreateNoWindow = true };
            ps.ArgumentList.Add("-NoProfile");
            ps.ArgumentList.Add("-Command");
            ps.ArgumentList.Add(script);
            return ps;
        }

        if (Os.IsMac)
        {
            var osascript = new ProcessStartInfo("osascript") { UseShellExecute = false, CreateNoWindow = true };
            osascript.ArgumentList.Add("-e");
            osascript.ArgumentList.Add(AppleScript(exe, args, home));
            return osascript;
        }

        var pkexec = new ProcessStartInfo("pkexec") { UseShellExecute = false };
        if (!string.IsNullOrEmpty(home))
        {
            pkexec.ArgumentList.Add("env");
            pkexec.ArgumentList.Add($"{HomeEnv}={home}");
        }
        pkexec.ArgumentList.Add(exe);
        foreach (var a in args) pkexec.ArgumentList.Add(a);
        return pkexec;
    }

    public static (int? Code, string? Error) Run(RightsAsk way, string exe, IReadOnlyList<string> args)
    {
        try
        {
            var home = Environment.GetEnvironmentVariable(HomeEnv);
            using var elevated = Process.Start(StartInfo(way, exe, args, home));
            if (elevated is null) return (null, "процесс не запустился");
            elevated.WaitForExit();
            return (elevated.ExitCode, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }
}
