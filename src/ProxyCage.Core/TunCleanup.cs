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

    public static bool IsOurEngineRunning(string runtimeConfigPath, string? root = null)
    {
        if (Os.IsWindows)
        {
            var home = root ?? Path.GetDirectoryName(runtimeConfigPath) ?? "";
            foreach (var name in new[] { Os.EngineFileName, Os.SingBoxFileName }.Distinct())
            {
                foreach (var (_, line) in Os.WindowsProcesses(name))
                {
                    if (IsOurEngineCommandLine(name, line, runtimeConfigPath, home))
                        return true;
                }
            }

            return false;
        }

        var (code, output) = Os.Run("pgrep", $"-f {runtimeConfigPath}", 5000);
        return code == 0 && output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length > 0;
    }

    private static bool IsOurEngineCommandLine(
        string processName, string commandLine, string runtimeConfigPath, string home) =>
        processName.Equals(Os.EngineFileName, StringComparison.OrdinalIgnoreCase)
        || commandLine.Contains(runtimeConfigPath, StringComparison.OrdinalIgnoreCase)
        || (home.Length > 0 && commandLine.Contains(home, StringComparison.OrdinalIgnoreCase));

    public static int KillOurProcesses(string runtimeConfigPath, Action<string>? log = null)
    {
        if (Os.IsWindows)
        {
            var home = Path.GetDirectoryName(runtimeConfigPath) ?? "";
            var killed = 0;
            foreach (var name in new[] { Os.EngineFileName, Os.SingBoxFileName }.Distinct())
            {
                foreach (var (pid, line) in Os.WindowsProcesses(name))
                {
                    if (!IsOurEngineCommandLine(name, line, runtimeConfigPath, home)) continue;

                    if (Os.Run("taskkill", $"/PID {pid} /F", 10000).Code == 0)
                    {
                        killed++;
                        log?.Invoke($"остановлен движок, процесс {pid}");
                    }
                }
            }

            // ceho-engine только наш; если Get-CimInstance недоступен — добиваем по имени.
            var (bulkCode, bulkOut) = Os.Run("taskkill", $"/IM {Os.EngineFileName} /F", 10000);
            if (bulkCode == 0)
            {
                var n = bulkOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Count(l => l.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase));
                if (n > killed)
                {
                    log?.Invoke($"остановлено процессов движка: {n}");
                    killed = n;
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

    public static int RemoveLeftovers(
        Action<string>? log = null, string? tunAddress = null, string? root = null,
        IReadOnlyCollection<string>? beforeStart = null, string? runtimeConfigPath = null)
        => Os.Kind switch
        {
            OsKind.Windows => RemoveGhostAdapters(log, tunAddress, root, beforeStart, runtimeConfigPath),
            OsKind.Linux => CleanLinux(log),
            _ => CleanMac(log),
        };

    /// <summary>
    /// Снять свой Wintun: сначала убить движок, потом отключить интерфейс, потом pnputil.
    /// aggressive — после FATAL «file already exists»: снимаем всё с нашим адресом, не только по записи.
    /// </summary>
    public static bool ReleaseOurs(
        string runtimeConfigPath,
        string? tunAddress,
        string? root,
        Action<string>? log = null,
        int attempts = 3,
        bool aggressive = false,
        IReadOnlyCollection<string>? beforeStart = null)
    {
        if (!Os.IsWindows) return RemoveLeftovers(log, tunAddress, root, beforeStart, runtimeConfigPath) >= 0;

        KillOurProcesses(runtimeConfigPath, log);
        WaitUntilEngineGone(runtimeConfigPath, 10000, log);

        var ourIp = tunAddress?.Split('/')[0].Trim();
        var recorded = Ours(root);
        var lookup = Adapter(tunAddress);
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            if (attempt > 1)
            {
                log?.Invoke($"повторная уборка Wintun, попытка {attempt}/{attempts}");
                Thread.Sleep(2000);
                KillOurProcesses(runtimeConfigPath, log);
                WaitUntilEngineGone(runtimeConfigPath, 5000, log);
            }

            var removed = 0;
            foreach (var id in WintunDevices())
            {
                var nic = lookup(id);
                if (!ShouldRemove(id, recorded, nic, ourIp, aggressive, beforeStart)) continue;

                if (nic is not null)
                    DisableInterface(nic.Name, log);

                if (!RemoveDevice(id, nic?.Name, log))
                    continue;

                removed++;
                if (!WaitUntilGone(id) || (nic is not null && !WaitUntilInterfaceGone(nic.Name)))
                    log?.Invoke($"устройство {id} ещё держится — продолжаю уборку");
            }

            if (removed > 0) Thread.Sleep(4000);

            if (!AnyOursLeft(recorded, lookup, ourIp, aggressive) && !TunnelAddressBusy(ourIp))
            {
                ClearOursFile(root);
                return true;
            }
        }

        return !AnyOursLeft(recorded, lookup, ourIp, aggressive) && !TunnelAddressBusy(ourIp);
    }

    public static int RemoveGhostAdapters(
        Action<string>? log = null, string? tunAddress = null, string? root = null,
        IReadOnlyCollection<string>? beforeStart = null, string? runtimeConfigPath = null)
    {
        if (runtimeConfigPath is not null)
        {
            KillOurProcesses(runtimeConfigPath, log);
            WaitUntilEngineGone(runtimeConfigPath, 8000, log);
        }

        var removed = 0;
        var lookup = Adapter(tunAddress);
        foreach (var (name, instanceId) in Removable(
                     WintunDevices(), lookup, Ours(root), log, beforeStart))
        {
            var nic = lookup(instanceId);
            if (nic is not null)
                DisableInterface(nic.Name, log);

            if (!RemoveDevice(instanceId, name, log)) continue;

            removed++;
            if (!WaitUntilGone(instanceId))
                log?.Invoke($"устройство {instanceId} ещё держится — движок может не встать");
        }

        if (removed > 0) Thread.Sleep(2500);
        return removed;
    }

    private static bool RemoveDevice(string instanceId, string? name, Action<string>? log)
    {
        var (code, output) = Os.Run("pnputil", $"/remove-device \"{instanceId}\"", 15000);
        if (code != 0)
        {
            log?.Invoke($"pnputil не снял {instanceId}: {output.Trim()}");
            return false;
        }

        log?.Invoke($"снят Wintun {name ?? "без интерфейса"}: {instanceId}");
        return true;
    }

    private static void DisableInterface(string name, Action<string>? log)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var (code, output) = Os.Run("netsh", $"interface set interface \"{name}\" disable", 10000);
        if (code != 0 && output.Length > 0)
            log?.Invoke($"не удалось выключить интерфейс {name}: {output.Trim()}");
    }

    private static bool ShouldRemove(
        string id,
        IReadOnlyCollection<string> recorded,
        Nic? nic,
        string? ourIp,
        bool aggressive,
        IReadOnlyCollection<string>? beforeStart)
    {
        if (NamedOurs(nic)) return true;

        if (aggressive)
        {
            if (recorded.Contains(id, StringComparer.OrdinalIgnoreCase)) return true;
            if (nic?.Ours == true) return true;
            if (NamedOurs(nic)) return true;
            if (nic is null && beforeStart?.Contains(id, StringComparer.OrdinalIgnoreCase) == true)
                return true;
            // Wintun без интерфейса после нашего сбоя — залипший ceho-tun, не Happ (у Happ интерфейс есть).
            if (nic is null) return true;
            return false;
        }

        return IsOursToKeep(id, recorded, nic, beforeStart);
    }

    /// <summary>Только наш ceho-tun по имени — Happ и прочие VPN не попадают.</summary>
    private static bool NamedOurs(Nic? nic) =>
        nic?.Name.StartsWith(InterfaceName, StringComparison.OrdinalIgnoreCase) == true;

    private static bool AnyOursLeft(
        IReadOnlyCollection<string> recorded,
        Func<string, Nic?> lookup,
        string? ourIp,
        bool aggressive)
    {
        foreach (var id in WintunDevices())
        {
            if (ShouldRemove(id, recorded, lookup(id), ourIp, aggressive, beforeStart: null))
                return true;
        }
        return false;
    }

    private static bool TunnelAddressBusy(string? ourIp)
    {
        if (ourIp is null) return false;
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Any(nic => HasAddress(nic, ourIp));
        }
        catch
        {
            return false;
        }
    }

    private static void WaitUntilEngineGone(string runtimeConfigPath, int timeoutMs, Action<string>? log)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (!EngineStillRunning(runtimeConfigPath)) return;
            Thread.Sleep(400);
        }
        log?.Invoke("движок ещё держит Wintun — принудительная остановка");
        KillOurProcesses(runtimeConfigPath, log);
    }

    private static bool EngineStillRunning(string runtimeConfigPath)
    {
        if (!Os.IsWindows) return false;

        var home = Path.GetDirectoryName(runtimeConfigPath) ?? "";
        foreach (var name in new[] { Os.EngineFileName, Os.SingBoxFileName }.Distinct())
        {
            foreach (var (pid, line) in Os.WindowsProcesses(name))
            {
                if (name.Equals(Os.EngineFileName, StringComparison.OrdinalIgnoreCase)
                    || line.Contains(runtimeConfigPath, StringComparison.OrdinalIgnoreCase)
                    || (home.Length > 0 && line.Contains(home, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }
        return false;
    }

    private static bool WaitUntilInterfaceGone(string name, int timeoutMs = 15000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            try
            {
                if (!NetworkInterface.GetAllNetworkInterfaces()
                        .Any(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            catch { return true; }
            Thread.Sleep(500);
        }
        return false;
    }

    private static void ClearOursFile(string? root)
    {
        if (root is null) return;
        try
        {
            var file = OursFile(root);
            if (File.Exists(file)) File.WriteAllText(file, "");
        }
        catch { }
    }

    /// <summary>Есть ли в журнале залипший Wintun после неудачной уборки.</summary>
    public static bool LogShowsStuckAdapter(int tailLines = 400)
    {
        var lines = Log.Tail(tailLines, LogView.All);
        var sawCleanup = false;
        foreach (var line in lines)
        {
            if (line.Contains("удалён залипший TUN", StringComparison.OrdinalIgnoreCase)
                || line.Contains("наш след без адаптера", StringComparison.OrdinalIgnoreCase)
                || line.Contains("снят Wintun", StringComparison.OrdinalIgnoreCase))
                sawCleanup = true;

            if (line.Contains("already exists", StringComparison.OrdinalIgnoreCase)
                && (line.Contains("FATAL", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("configure tun interface", StringComparison.OrdinalIgnoreCase)))
                return true;

            if (sawCleanup && line.Contains("движок не устоял", StringComparison.OrdinalIgnoreCase)
                           && line.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// pnputil отвечает раньше, чем Windows успевает убрать устройство, а движок сразу за нами
    /// создаёт своё с тем же именем и ловит «файл уже существует». Поэтому ждём по-настоящему.
    /// </summary>
    private static bool WaitUntilGone(string instanceId, int timeoutMs = 25000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (!WintunDevices().Contains(instanceId, StringComparer.OrdinalIgnoreCase)) return true;
            Thread.Sleep(500);
        }
        return false;
    }

    /// <summary>Сетевой интерфейс за Wintun-устройством, если он вообще есть.</summary>
    public sealed record Nic(string Name, bool Up, bool Ours);

    /// <summary>Все Wintun-устройства системы, включая те, у которых адаптера уже нет.</summary>
    public static IReadOnlyList<string> Devices() => Os.IsWindows ? WintunDevices() : Array.Empty<string>();

    /// <summary>
    /// Убирать можно только то, что создали сами, поэтому запоминаем устройство после
    /// запуска движка: то, что держит наш адрес, то, что уже было записано, и то, что
    /// появилось за этот старт, если это не живой чужой туннель.
    /// </summary>
    public static void Remember(
        string root, string? tunAddress = null, Action<string>? log = null,
        IReadOnlyCollection<string>? beforeStart = null)
    {
        if (!Os.IsWindows) return;

        var adapter = Adapter(tunAddress);
        var recorded = Ours(root);
        var mine = WintunDevices()
            .Where(id => IsOursToKeep(id, recorded, adapter(id), beforeStart))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        try { File.WriteAllLines(OursFile(root), mine); }
        catch (Exception ex) { log?.Invoke($"не запомнил свой TUN-адаптер: {ex.Message}"); }
    }

    /// <summary>
    /// Своё — то, что уже записано, или то, что сейчас держит наш адрес. Адрес должен быть
    /// своим, не заводским 172.19.0.1: иначе Happ на том же адресе попадёт в список.
    /// </summary>
    public static IReadOnlyList<string> Mine(
        IEnumerable<string> known,
        IEnumerable<string> recorded, Func<string, bool> ours)
    {
        var written = recorded.ToList();

        return known
            .Where(id => written.Contains(id, StringComparer.OrdinalIgnoreCase) || ours(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Можно снимать: записали сами, держит наш уникальный адрес, устройство без интерфейса
    /// (залипший след — в журнале «наш след без адаптера»), или появилось за этот старт
    /// и это не живой чужой туннель. Чужой happ-tun, даже выключенный, сюда не попадает:
    /// у него интерфейс есть.
    /// </summary>
    public static bool IsOursToKeep(
        string id,
        IReadOnlyCollection<string> recorded,
        Nic? nic,
        IReadOnlyCollection<string>? beforeStart = null)
    {
        if (NamedOurs(nic)) return true;
        if (recorded.Contains(id, StringComparer.OrdinalIgnoreCase)) return true;
        if (nic?.Ours == true) return true;
        if (beforeStart is null) return false;

        // Уже было до старта. Без интерфейса — наш залипший след: из‑за него движок
        // падает на «файл уже существует». Чужой адаптер (Happ) сюда не попадает —
        // у него имя и интерфейс, пусть и выключенный.
        if (beforeStart.Contains(id, StringComparer.OrdinalIgnoreCase))
            return nic is null;

        // Появилось, пока мы поднимали движок. Живой чужой туннель не берём.
        return nic is not { Ours: false, Up: true };
    }

    /// <summary>Устройства, записанные нами как свои. Всё остальное — чужое имущество.</summary>
    public static IReadOnlyList<string> Ours(string? root)
    {
        if (root is null) return Array.Empty<string>();
        try
        {
            var file = OursFile(root);
            return File.Exists(file)
                ? File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0).ToList()
                : Array.Empty<string>();
        }
        catch { return Array.Empty<string>(); }
    }

    private static string OursFile(string root) => Path.Combine(root, "tun-devices.txt");

    private static IReadOnlyList<string> WintunDevices()
    {
        var (_, output) = Os.Run("pnputil", "/enum-devices /class Net", 20000);
        var ids = new List<string>();
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            var at = line.IndexOf(@"SWD\Wintun\", StringComparison.OrdinalIgnoreCase);
            if (at >= 0) ids.Add(line[at..].Trim());
        }
        return ids;
    }

    /// <summary>
    /// Что можно убрать. Записанное как своё, либо адаптер на нашем уникальном адресе.
    /// Живой чужой туннель и чужой на заводском 172.19.0.1 — нет: так мы уже сносили Happ.
    /// </summary>
    public static IReadOnlyList<(string Name, string InstanceId)> Removable(
        IEnumerable<string> deviceIds, Func<string, Nic?> adapter,
        IReadOnlyCollection<string> ours, Action<string>? log = null,
        IReadOnlyCollection<string>? beforeStart = null)
    {
        var removable = new List<(string, string)>();
        foreach (var id in deviceIds)
        {
            if (!id.Contains(@"SWD\WINTUN\", StringComparison.OrdinalIgnoreCase)) continue;

            var nic = adapter(id);
            if (!IsOursToKeep(id, ours, nic, beforeStart))
            {
                log?.Invoke($"чужое устройство {id} не наше, не трогаю");
                continue;
            }

            removable.Add((nic?.Name ?? "наш след без адаптера", id));
        }
        return removable;
    }

    /// <summary>
    /// Wintun даёт устройству и сетевому интерфейсу один и тот же GUID — по нему устройство
    /// из pnputil и находит свой интерфейс. Имена для этого не нужны: они локализованные
    /// и в консоли приезжают битыми.
    /// </summary>
    private static Func<string, Nic?> Adapter(string? tunAddress)
    {
        var ourIp = tunAddress?.Split('/')[0].Trim();

        return deviceId =>
        {
            var guid = GuidOf(deviceId);
            if (guid is null) return null;

            // Устройство без интерфейса — самый опасный вывод: если система просто не успела
            // показать интерфейс, мы снесём живой чужой туннель. Поэтому спрашиваем дважды.
            return Look(guid, ourIp) ?? Again(guid, ourIp);
        };
    }

    private static Nic? Again(string guid, string? ourIp)
    {
        Thread.Sleep(700);
        return Look(guid, ourIp);
    }

    private static Nic? Look(string guid, string? ourIp)
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!string.Equals(nic.Id, guid, StringComparison.OrdinalIgnoreCase)) continue;

                var ours = nic.Name.StartsWith(InterfaceName, StringComparison.OrdinalIgnoreCase)
                           || (ourIp is not null
                               && !CehoConfig.SharesSingBoxTun($"{ourIp}/30")
                               && HasAddress(nic, ourIp));

                return new Nic(nic.Name, nic.OperationalStatus == OperationalStatus.Up, ours);
            }
        }
        catch
        {
            // Не смогли спросить систему — считаем туннель чужим и живым, чтобы не снести лишнее.
            return new Nic("неизвестный", true, false);
        }
        return null;
    }

    private static string? GuidOf(string deviceId)
    {
        var at = deviceId.LastIndexOf('{');
        return at < 0 ? null : deviceId[at..].Trim();
    }

    private static bool HasAddress(NetworkInterface nic, string ip)
    {
        try
        {
            return nic.GetIPProperties().UnicastAddresses
                .Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork
                          && a.Address.ToString() == ip);
        }
        catch
        {
            return false;
        }
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
