using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace ProxyCage.Core;

public static class TunCleanup
{
    public const string InterfaceName = "ceho-tun";

    public const int Iproute2TableIndex = 2122;
    public const int Iproute2RuleIndex = 9100;

    private const int RuleSpan = 16;

    public static int KillOurProcesses(string runtimeConfigPath, Action<string>? log = null)
    {
        if (Os.IsWindows)
        {
            var (_, list) = Os.Run("wmic",
                "process where \"name='sing-box.exe'\" get processid,commandline /format:csv", 15000);
            var killed = 0;
            foreach (var line in list.Split('\n'))
            {
                if (!line.Contains(runtimeConfigPath, StringComparison.OrdinalIgnoreCase)) continue;
                var pid = line.Split(',').LastOrDefault()?.Trim();
                if (int.TryParse(pid, out var id) && Os.Run("taskkill", $"/PID {id} /F", 10000).Code == 0)
                {
                    killed++;
                    log?.Invoke($"остановлен движок, процесс {id}");
                }
            }
            return killed;
        }

        var (code, output) = Os.Run("pgrep", $"-f {runtimeConfigPath}", 10000);
        if (code != 0) return 0;

        var stopped = 0;
        foreach (var raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(raw.Trim(), out var pid) || pid == Environment.ProcessId) continue;
            if (Os.Run("kill", $"-9 {pid}", 5000).Code == 0)
            {
                stopped++;
                log?.Invoke($"остановлен движок, процесс {pid}");
            }
        }
        return stopped;
    }

    public static int RemoveLeftovers(Action<string>? log = null, string? tunAddress = null) => Os.Kind switch
    {
        OsKind.Windows => RemoveGhostAdapters(log, tunAddress),
        OsKind.Linux => CleanLinux(log),
        _ => CleanMac(log),
    };

    public static int RemoveGhostAdapters(Action<string>? log = null, string? tunAddress = null)
    {
        var removed = 0;
        foreach (var (name, instanceId) in OurAdapters(log, tunAddress))
        {
            var (code, _) = Os.Run("pnputil", $"/remove-device \"{instanceId}\"", 15000);
            if (code == 0)
            {
                removed++;
                log?.Invoke($"удалён залипший TUN-адаптер {name}: {instanceId}");
            }
        }
        return removed;
    }

    /// <summary>
    /// На той же Wintun работают и другие клиенты (Happ, Nekoray и прочие), а их адаптеры
    /// с виду не отличаются от нашего. Поэтому берём только наш: по имени интерфейса,
    /// а для туннелей, поднятых старыми версиями, — по адресу. Не узнали своего — не трогаем ничего.
    /// </summary>
    private static IEnumerable<(string Name, string InstanceId)> OurAdapters(
        Action<string>? log, string? tunAddress) =>
        Mine(WindowsNetAdapters(log), tunAddress is null ? null : InterfaceWithAddress(tunAddress), log);

    /// <summary>Отбор своих из всех адаптеров системы. Вынесен отдельно, чтобы его можно было проверить.</summary>
    public static IReadOnlyList<(string Name, string InstanceId)> Mine(
        IEnumerable<(string Name, string InstanceId)> adapters, string? ourInterface, Action<string>? log = null)
    {
        var mine = new List<(string, string)>();
        foreach (var (name, id) in adapters)
        {
            if (!id.Contains(@"SWD\WINTUN\", StringComparison.OrdinalIgnoreCase)) continue;

            var ours = name.StartsWith(InterfaceName, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(name, ourInterface, StringComparison.OrdinalIgnoreCase);

            if (ours) mine.Add((name, id));
            else log?.Invoke($"чужой TUN-адаптер {name} не трогаю");
        }
        return mine;
    }

    private static IEnumerable<(string Name, string InstanceId)> WindowsNetAdapters(Action<string>? log)
    {
        const string script = "Get-NetAdapter -IncludeHidden | ForEach-Object { $_.Name + '|' + $_.PnPDeviceID }";
        var (code, output) = Os.Run("powershell", $"-NoProfile -NonInteractive -Command \"{script}\"", 20000);
        if (code != 0)
        {
            log?.Invoke("не удалось перечислить сетевые адаптеры, следы оставляю как есть");
            return Array.Empty<(string, string)>();
        }

        var found = new List<(string, string)>();
        foreach (var raw in output.Split('\n'))
        {
            var parts = raw.Trim().Split('|', 2);
            if (parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0)
                found.Add((parts[0].Trim(), parts[1].Trim()));
        }
        return found;
    }

    /// <summary>Имя интерфейса, на котором висит этот адрес: так узнаётся наш старый туннель.</summary>
    public static string? InterfaceWithAddress(string address)
    {
        var ip = address.Split('/')[0].Trim();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork
                    && unicast.Address.ToString() == ip)
                    return nic.Name;
        }
        catch { }
        return null;
    }

    private static int CleanLinux(Action<string>? log)
    {
        var removed = 0;

        removed += DeleteStaleRules(log, ipv6: false);

        removed += DeleteStaleRules(log, ipv6: true);

        Os.Run("ip", $"route flush table {Iproute2TableIndex}", 10000);
        Os.Run("ip", $"-6 route flush table {Iproute2TableIndex}", 10000);

        var (linkCode, _) = Os.Run("ip", $"link show {InterfaceName}", 10000);
        if (linkCode == 0)
        {
            var (delCode, delOut) = Os.Run("ip", $"link delete {InterfaceName}", 10000);
            if (delCode == 0)
            {
                removed++;
                log?.Invoke($"удалён залипший интерфейс {InterfaceName}");
            }
            else log?.Invoke($"не удалось удалить {InterfaceName}: {delOut}");
        }

        return removed;
    }

    private static int DeleteStaleRules(Action<string>? log, bool ipv6)
    {
        var family = ipv6 ? "-6 " : "";
        var removed = 0;

        foreach (var pref in StaleRulePriorities(log, ipv6))
        {
            var atThisPriority = 0;
            while (atThisPriority < RuleSpan && Os.Run("ip", $"{family}rule del pref {pref}", 10000).Code == 0)
                atThisPriority++;

            if (atThisPriority == 0) continue;
            removed += atThisPriority;
            log?.Invoke($"снято залипших правил маршрутизации: {atThisPriority}, приоритет {pref}" +
                        (ipv6 ? " (IPv6)" : ""));
        }
        return removed;
    }

    private static IEnumerable<int> StaleRulePriorities(Action<string>? log, bool ipv6 = false)
    {
        var (code, output) = Os.Run("ip", (ipv6 ? "-6 " : "") + "-j rule show", 10000);
        if (code != 0 || output.Length == 0) return Array.Empty<int>();

        var found = new List<int>();
        try
        {
            using var doc = JsonDocument.Parse(output);
            foreach (var rule in doc.RootElement.EnumerateArray())
            {
                if (!rule.TryGetProperty("priority", out var prio) || prio.ValueKind != JsonValueKind.Number)
                    continue;
                var p = prio.GetInt32();
                if (p is 0 or 32766 or 32767) continue;

                var inOurRange = p >= Iproute2RuleIndex && p < Iproute2RuleIndex + RuleSpan;
                var toOurTable = rule.TryGetProperty("table", out var t)
                                 && t.ToString() == Iproute2TableIndex.ToString();

                if (inOurRange || toOurTable) found.Add(p);
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"не разобрал вывод ip rule: {ex.Message}");
        }
        return found.Distinct();
    }

    private static int CleanMac(Action<string>? log)
    {
        return 0;
    }
}
