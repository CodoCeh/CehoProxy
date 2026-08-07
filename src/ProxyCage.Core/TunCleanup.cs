using System.Text.Json;

namespace ProxyCage.Core;

/// <summary>
/// Самолечение перед стартом.
///
/// Штатного завершения может не случиться в принципе: краш, BSOD, потеря питания,
/// kill -9. После него в системе остаётся мусор, и следующий старт либо падает,
/// либо — что хуже — оставляет машину без сети. Поэтому чистим ПЕРЕД каждым запуском,
/// а не надеемся на корректный выход.
///
/// Windows: WinTun-адаптер остаётся «призраком», и старт падает на
/// «configure tun interface: Cannot create a file when that file already exists».
/// Linux: остаются ip rule и таблица маршрутизации, и часть из них блэкхолит трафик —
/// сеть машины ложится целиком, а не только у изолированных приложений.
/// macOS: utun принадлежит процессу и исчезает вместе с ним, маршруты снимает ядро.
///
/// Чтобы не задеть ЧУЖОЙ sing-box (у пользователя вполне может работать свой VPN),
/// имя интерфейса и индексы таблиц у нас собственные и отличаются от умолчаний движка.
/// </summary>
public static class TunCleanup
{
    /// <summary>Имя нашего TUN на Linux. macOS выдаёт utunN сам, Windows именует адаптер сам.</summary>
    public const string LinuxInterfaceName = "ceho-tun";

    /// <summary>Своя таблица маршрутизации и свой диапазон приоритетов правил — не умолчания sing-box.</summary>
    public const int Iproute2TableIndex = 2122;
    public const int Iproute2RuleIndex = 9100;

    /// <summary>Сколько приоритетов подряд от <see cref="Iproute2RuleIndex"/> занимает движок.</summary>
    private const int RuleSpan = 16;

    /// <summary>
    /// Принудительно завершает НАШ движок и НАШУ службу, если после мягкой остановки они живы.
    ///
    /// Нужно при удалении: иначе на машине остаётся работающий туннель и открытая панель,
    /// хотя файлов продукта уже нет — поймано живьём. Опознаём строго по пути нашего конфига,
    /// поэтому чужой sing-box на той же машине не пострадает.
    /// </summary>
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

    /// <summary>Убирает следы прошлого запуска. Вызывать только когда движок не работает.</summary>
    public static int RemoveLeftovers(Action<string>? log = null) => Os.Kind switch
    {
        OsKind.Windows => RemoveGhostAdapters(log),
        OsKind.Linux => CleanLinux(log),
        _ => CleanMac(log),
    };

    // ── Windows ───────────────────────────────────────────────────────

    public static int RemoveGhostAdapters(Action<string>? log = null)
    {
        var removed = 0;
        foreach (var instanceId in FindSingTunInstanceIds(log))
        {
            var (code, _) = Os.Run("pnputil", $"/remove-device \"{instanceId}\"", 15000);
            if (code == 0)
            {
                removed++;
                log?.Invoke($"удалён залипший TUN-адаптер: {instanceId}");
            }
        }
        return removed;
    }

    private static IEnumerable<string> FindSingTunInstanceIds(Action<string>? log)
    {
        // Локаль системы может быть любой, поэтому опираемся не на подписи полей,
        // а на класс Net и на то, что InstanceId у WinTun всегда начинается с SWD\WINTUN\.
        var (_, output) = Os.Run("pnputil", "/enum-devices /class Net", 15000);
        var ids = new List<string>();
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            var idx = line.IndexOf(@"SWD\WINTUN\", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) ids.Add(line[idx..].Trim());
        }
        return ids;
    }

    // ── Linux ─────────────────────────────────────────────────────────

    private static int CleanLinux(Action<string>? log)
    {
        var removed = 0;

        removed += DeleteStaleRules(log, ipv6: false);
        // ip -6 держит собственный набор правил с теми же приоритетами
        removed += DeleteStaleRules(log, ipv6: true);

        Os.Run("ip", $"route flush table {Iproute2TableIndex}", 10000);
        Os.Run("ip", $"-6 route flush table {Iproute2TableIndex}", 10000);

        var (linkCode, _) = Os.Run("ip", $"link show {LinuxInterfaceName}", 10000);
        if (linkCode == 0)
        {
            var (delCode, delOut) = Os.Run("ip", $"link delete {LinuxInterfaceName}", 10000);
            if (delCode == 0)
            {
                removed++;
                log?.Invoke($"удалён залипший интерфейс {LinuxInterfaceName}");
            }
            else log?.Invoke($"не удалось удалить {LinuxInterfaceName}: {delOut}");
        }

        return removed;
    }

    /// <summary>
    /// Проверено живьём: движок создаёт по НЕСКОЛЬКУ правил с одним приоритетом, а
    /// «ip rule del pref N» снимает ровно одно. Одного прохода не хватает, и после каждой
    /// аварии в системе копился бы новый слой правил маршрутизации. Поэтому по каждому
    /// приоритету удаляем, пока команда не начнёт возвращать ошибку.
    /// </summary>
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

    /// <summary>
    /// Наши правила опознаём по своей таблице и по своему диапазону приоритетов.
    /// Ни то, ни другое не совпадает с умолчаниями sing-box, поэтому чужой туннель не пострадает.
    /// Приоритеты 0 и 32766/32767 — системные local/main/default, их не трогаем никогда.
    /// </summary>
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

    // ── macOS ─────────────────────────────────────────────────────────

    private static int CleanMac(Action<string>? log)
    {
        // utun принадлежит открытому сокету процесса: умирает процесс — исчезает интерфейс,
        // а вместе с ним ядро снимает и маршруты, которые на него ссылались.
        // Проверено kill -9: ни интерфейса, ни маршрутов не остаётся, чистить нечего.
        return 0;
    }
}
