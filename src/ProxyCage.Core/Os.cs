using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ProxyCage.Core;

public enum OsKind { Windows, Linux, Mac }

public static class Os
{
    public static OsKind Kind =>
        OperatingSystem.IsWindows() ? OsKind.Windows :
        OperatingSystem.IsMacOS() ? OsKind.Mac : OsKind.Linux;

    public static bool IsWindows => Kind == OsKind.Windows;
    public static bool IsMac => Kind == OsKind.Mac;
    public static bool IsLinux => Kind == OsKind.Linux;

    public static string DefaultRoot => Kind switch
    {
        OsKind.Windows => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CehoProxy"),
        OsKind.Mac => "/Library/Application Support/CehoProxy",
        _ => "/var/lib/cehoproxy",
    };

    public static string SingBoxFileName => IsWindows ? "sing-box.exe" : "sing-box";

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

    public static IReadOnlyList<string> SystemDnsServers(string tunAddress)
    {
        var ours = tunAddress.Split('/')[0];
        var oursPrefix = ours[..(ours.LastIndexOf('.') + 1)];

        var found = Kind switch
        {
            OsKind.Windows => OperatingSystem.IsWindows() ? FromWindowsDns() : Array.Empty<string>(),
            OsKind.Mac => FromMacDns(),
            _ => FromResolvConf(),
        };

        var candidates = found
            .Where(a => a.Count(c => c == '.') == 3
                        && System.Net.IPAddress.TryParse(a, out var ip)
                        && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .Where(a => a != "0.0.0.0" && !a.StartsWith("127.") && !a.StartsWith(oursPrefix, StringComparison.Ordinal))
            .Distinct()
            .ToList();

        var physical = candidates.Where(a => !LooksLikeTunnelAddress(a)).Where(Answers).ToList();
        if (physical.Count > 0) return physical.Take(2).ToList();

        return new List<string> { PublicResolver };
    }

    public const string PublicResolver = "1.1.1.1";

    private static bool LooksLikeTunnelAddress(string address)
    {
        if (address.StartsWith("10.", StringComparison.Ordinal)) return true;
        if (!address.StartsWith("172.", StringComparison.Ordinal)) return false;
        var second = address.Split('.').ElementAtOrDefault(1);
        return int.TryParse(second, out var octet) && octet is >= 16 and <= 31;
    }

    private static bool Answers(string server)
    {
        try
        {
            using var udp = new System.Net.Sockets.UdpClient();
            udp.Client.ReceiveTimeout = 2000;
            udp.Connect(server, 53);

            var query = new byte[] {
                0x2a, 0x2a, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                7, (byte)'e', (byte)'x', (byte)'a', (byte)'m', (byte)'p', (byte)'l', (byte)'e',
                3, (byte)'c', (byte)'o', (byte)'m', 0x00, 0x00, 0x01, 0x00, 0x01,
            };
            udp.Send(query, query.Length);

            var from = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
            var answer = udp.Receive(ref from);
            if (answer.Length < 12 || answer[0] != 0x2a || answer[1] != 0x2a) return false;
            if ((answer[2] & 0x80) == 0) return false;
            if ((answer[3] & 0x0F) != 0) return false;

            var answers = (answer[6] << 8) | answer[7];
            return answers > 0;
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> FromWindowsDns()
    {
        const string script =
            "Get-NetAdapter -Physical -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'Up' } | " +
            "ForEach-Object { (Get-DnsClientServerAddress -InterfaceIndex $_.ifIndex -AddressFamily IPv4 " +
            "-ErrorAction SilentlyContinue).ServerAddresses }; " +
            "Get-DnsClientServerAddress -AddressFamily IPv4 | Where-Object { $_.ServerAddresses } | " +
            "ForEach-Object { $_.ServerAddresses }";

        var (code, output) = Run("powershell", $"-NoProfile -Command \"{script}\"", 20000);
        return code == 0 ? output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim())
                         : Array.Empty<string>();
    }

    private static IEnumerable<string> FromMacDns()
    {
        var (code, output) = Run("scutil", "--dns", 15000);
        if (code != 0) return Array.Empty<string>();
        return output.Split('\n')
            .Where(l => l.Contains("nameserver[", StringComparison.Ordinal))
            .Select(l => l[(l.IndexOf(':') + 1)..].Trim());
    }

    private static IEnumerable<string> FromResolvConf()
    {
        try
        {
            return File.ReadAllLines("/etc/resolv.conf")
                .Where(l => l.StartsWith("nameserver", StringComparison.Ordinal))
                .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? "");
        }
        catch { return Array.Empty<string>(); }
    }

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

    public static bool IsRunnable(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            var target = RealPath(path);
            if (!File.Exists(target)) return false;
            if (IsWindows) return true;

    #pragma warning disable CA1416
        var mode = File.GetUnixFileMode(target);
#pragma warning restore CA1416
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
