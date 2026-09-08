using System.Net.NetworkInformation;

namespace ProxyCage.Core;

/// <summary>
/// Чужие настройки на этой же машине, которые ломают сеть, но выглядят как наша вина:
/// системный прокси Windows и туннели других VPN-клиентов. Мы их только читаем.
/// </summary>
public static class SystemProxy
{
    private const string Key = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    /// <summary>
    /// Адрес включённого системного прокси, если он смотрит в локальный порт, где никто не слушает.
    /// Наш собственный порт не считаем: если молчит он, об этом уже скажут другие проверки.
    /// </summary>
    public static string? DeadLoopbackProxy(int ourPort) =>
        Os.IsWindows
            ? DeadAmong(ReadValue("ProxyEnable"), ReadValue("ProxyServer"), ourPort, PortIsListening)
            : null;

    /// <summary>
    /// Системный прокси Windows включён и смотрит на наш mixed-порт. CehoProxy его не ставит;
    /// Electron-приложения (Cursor) начинают ходить через 127.0.0.1:2080 параллельно с TUN.
    /// </summary>
    public static string? EnabledOnOurPort(int ourPort) =>
        Os.IsWindows
            ? EnabledOnOurPortAmong(ReadValue("ProxyEnable"), ReadValue("ProxyServer"), ourPort)
            : null;

    public static string? EnabledOnOurPortAmong(string? proxyEnable, string? proxyServer, int ourPort)
    {
        if (proxyEnable is null || !Enabled(proxyEnable) || proxyServer is null) return null;

        foreach (var address in Addresses(proxyServer))
        {
            var (host, port) = Split(address);
            if (port is null || !IsLoopback(host) || port != ourPort) continue;
            return $"{host}:{port}";
        }
        return null;
    }

    /// <summary>Разбор настройки без обращения к системе: так это можно проверить тестом.</summary>
    public static string? DeadAmong(string? proxyEnable, string? proxyServer, int ourPort,
        Func<int, bool> listening)
    {
        if (proxyEnable is null || !Enabled(proxyEnable) || proxyServer is null) return null;

        foreach (var address in Addresses(proxyServer))
        {
            var (host, port) = Split(address);
            if (port is null || !IsLoopback(host) || port == ourPort) continue;
            if (!listening(port.Value)) return $"{host}:{port}";
        }
        return null;
    }

    /// <summary>Работающие туннели, которые не наши: по ним видно, что на машине два клиента.</summary>
    public static IReadOnlyList<string> OtherTunnels(string? ourInterface)
    {
        var found = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.Name.StartsWith(TunCleanup.InterfaceName, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(nic.Name, ourInterface, StringComparison.OrdinalIgnoreCase)) continue;

                var isVpn = nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel
                    || IsVpnAdapter(nic.Description)
                    || IsVpnAdapter(nic.Name);
                if (!isVpn) continue;

                found.Add(nic.Name);
            }
        }
        catch { }
        return found;
    }

    private static bool IsVpnAdapter(string text) =>
        text.Contains("tun", StringComparison.OrdinalIgnoreCase)
        || text.Contains("tap", StringComparison.OrdinalIgnoreCase)
        || text.Contains("vpn", StringComparison.OrdinalIgnoreCase)
        || text.Contains("wireguard", StringComparison.OrdinalIgnoreCase)
        || text.Contains("wintun", StringComparison.OrdinalIgnoreCase)
        || text.Contains("softether", StringComparison.OrdinalIgnoreCase)
        || text.Contains("amnezia", StringComparison.OrdinalIgnoreCase)
        || text.Contains("tailscale", StringComparison.OrdinalIgnoreCase);

    private static bool Enabled(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToInt32(text[2..], 16) != 0;
        return int.TryParse(text, out var n) && n != 0;
    }

    /// <summary>«socks=127.0.0.1:10808;http=…» или просто «127.0.0.1:8080».</summary>
    private static IEnumerable<string> Addresses(string servers)
    {
        foreach (var part in servers.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var value = part.Contains('=') ? part[(part.IndexOf('=') + 1)..] : part;
            value = value.Trim();
            if (value.Length > 0) yield return value;
        }
    }

    private static (string Host, int? Port) Split(string address)
    {
        var at = address.LastIndexOf(':');
        if (at <= 0 || !int.TryParse(address[(at + 1)..], out var port)) return (address, null);
        return (address[..at], port);
    }

    private static bool IsLoopback(string host) =>
        host is "localhost" || host.StartsWith("127.", StringComparison.Ordinal);

    private static bool PortIsListening(int port)
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(e => e.Port == port);
        }
        catch
        {
            return true;
        }
    }

    private static string? ReadValue(string name)
    {
        var (code, output) = Os.Run("reg", $"query \"{Key}\" /v {name}", 10000);
        if (code != 0) return null;

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith(name, StringComparison.OrdinalIgnoreCase)) continue;

            // «ProxyServer    REG_SZ    socks=127.0.0.1:10808»
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3) return string.Join(' ', parts[2..]);
        }
        return null;
    }
}
