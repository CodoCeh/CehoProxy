using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace ProxyCage.Core;

public static class ForwardingGuard
{
    private static string RestoreFile(string root) => Path.Combine(root, "forwarding-restore.txt");

    public static void Apply(string root, string? tunAddress, Action<string>? log)
    {
        if (!Os.IsWindows) return;
        try
        {
            var alias = EgressAlias(Os.PhysicalBindAddress(tunAddress));
            if (alias is null || !IsEnabled(alias)) return;

            File.WriteAllText(RestoreFile(root), alias);
            Os.Run("powershell", Script(alias, "Disabled"), 20000);
            log?.Invoke($"пересылка IP на внешней сетевой карте {alias} выключена на время работы туннеля (включена роль маршрутизации)");
        }
        catch (Exception ex)
        {
            log?.Invoke("пересылка IP: не удалось проверить: " + ex.Message);
        }
    }

    public static void Restore(string root, Action<string>? log)
    {
        if (!Os.IsWindows) return;
        try
        {
            var file = RestoreFile(root);
            if (!File.Exists(file)) return;
            var alias = File.ReadAllText(file).Trim();
            if (alias.Length > 0)
            {
                Os.Run("powershell", Script(alias, "Enabled"), 20000);
                log?.Invoke($"пересылка IP на сетевой карте {alias} включена обратно");
            }
            File.Delete(file);
        }
        catch (Exception ex)
        {
            log?.Invoke("пересылка IP: не удалось вернуть: " + ex.Message);
        }
    }

    private static string Script(string alias, string state) =>
        "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"Set-NetIPInterface -InterfaceAlias '" +
        alias.Replace("'", "''") + "' -AddressFamily IPv4 -Forwarding " + state + "\"";

    [DllImport("iphlpapi.dll")]
    private static extern uint GetIpInterfaceEntry(IntPtr row);

    private static bool IsEnabled(string alias)
    {
        var buffer = IntPtr.Zero;
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces().First(n => n.Name == alias);
            buffer = Marshal.AllocHGlobal(256);
            for (var i = 0; i < 256; i++) Marshal.WriteByte(buffer, i, 0);
            Marshal.WriteInt16(buffer, 0, 2);
            Marshal.WriteInt32(buffer, 16, nic.GetIPProperties().GetIPv4Properties().Index);
            if (GetIpInterfaceEntry(buffer) == 0) return Marshal.ReadByte(buffer, 41) != 0;
        }
        catch { }
        finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
        var (code, output) = Os.Run("powershell",
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"(Get-NetIPInterface -InterfaceAlias '" +
            alias.Replace("'", "''") + "' -AddressFamily IPv4).Forwarding\"", 20000);
        return code == 0 && output.Trim().Equals("Enabled", StringComparison.OrdinalIgnoreCase);
    }

    private static string? EgressAlias(IPAddress? address)
    {
        if (address is null) return null;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            if (nic.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(address)))
                return nic.Name;
        return null;
    }
}
