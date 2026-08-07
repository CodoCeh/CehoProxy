using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ProxyCage.Core;

public enum OsKind { Windows, Linux, Mac }

/// <summary>
/// Всё, что расходится между Windows, Linux и macOS: пути, права, поиск внешних программ.
/// Платформенный шов собран здесь и в четырёх соседних файлах
/// (SingBoxProcess, TunCleanup, Autostart, AppDetector) — больше нигде.
/// </summary>
public static class Os
{
    public static OsKind Kind =>
        OperatingSystem.IsWindows() ? OsKind.Windows :
        OperatingSystem.IsMacOS() ? OsKind.Mac : OsKind.Linux;

    public static bool IsWindows => Kind == OsKind.Windows;
    public static bool IsMac => Kind == OsKind.Mac;
    public static bool IsLinux => Kind == OsKind.Linux;

    /// <summary>Папка настроек по умолчанию. Каноничное для каждой системы место, доступное только root.</summary>
    public static string DefaultRoot => Kind switch
    {
        OsKind.Windows => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CehoProxy"),
        OsKind.Mac => "/Library/Application Support/CehoProxy",
        _ => "/var/lib/cehoproxy",
    };

    public static string SingBoxFileName => IsWindows ? "sing-box.exe" : "sing-box";

    /// <summary>
    /// Движок ищем в папке настроек, рядом с собой и в PATH. На Unix его обычно ставят
    /// пакетным менеджером, и требовать копию в своей папке — лишний тупик на ровном месте.
    /// </summary>
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

    /// <summary>
    /// Настоящие DNS-серверы машины — те, которыми она пользуется БЕЗ нашего туннеля.
    ///
    /// Нужны, чтобы не устроить петлю: с поднятым TUN система спрашивает наш туннель,
    /// а туннель, если сказать ему «спрашивай систему», спрашивает её же. Поймано живьём
    /// на Windows: при включённой защите машина переставала резолвить вообще всё.
    /// Адреса из подсети нашего туннеля отбрасываются — это и есть петля.
    /// </summary>
    public static IReadOnlyList<string> SystemDnsServers(string tunAddress)
    {
        var ours = tunAddress.Split('/')[0];
        var oursPrefix = ours[..(ours.LastIndexOf('.') + 1)];

        var found = Kind switch
        {
            // ветка выполняется только на Windows, но анализатор этого не выводит
            OsKind.Windows => OperatingSystem.IsWindows() ? FromWindowsDns() : Array.Empty<string>(),
            OsKind.Mac => FromMacDns(),
            _ => FromResolvConf(),
        };

        var candidates = found
            // четыре октета через точку: «53» тоже разбирается как адрес, и такой
            // мусор уезжал в конфиг движка
            .Where(a => a.Count(c => c == '.') == 3
                        && System.Net.IPAddress.TryParse(a, out var ip)
                        && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .Where(a => a != "0.0.0.0" && !a.StartsWith("127.") && !a.StartsWith(oursPrefix, StringComparison.Ordinal))
            .Distinct()
            .ToList();

        // Порядок важнее самого списка.
        //
        // 1. DNS физической сетевой карты, если он отвечает, — он переживёт наш туннель.
        // 2. Публичный резолвер напрямую — работает всегда, пока есть интернет.
        // 3. Всё остальное — только если ничего лучше нет.
        //
        // Почему не берём просто «тот, что отвечает»: на машине владельца отвечал только
        // DNS ЧУЖОГО туннеля (happ-tun, 172.18.0.2). Наш TUN перекраивает маршруты, чужой
        // туннель ломается — и его резолвер пропадает вместе с ним. Машина остаётся без
        // имён целиком. Поймано живьём на Windows.
        var physical = candidates.Where(a => !LooksLikeTunnelAddress(a)).Where(Answers).ToList();
        if (physical.Count > 0) return physical.Take(2).ToList();

        return new List<string> { PublicResolver };
    }

    /// <summary>Публичный резолвер: к нему ходим напрямую, когда своего рабочего нет.</summary>
    public const string PublicResolver = "1.1.1.1";

    /// <summary>
    /// Похоже на адрес внутри туннеля, а не на настоящий шлюз сети.
    /// Туннельные клиенты живут в 10/8 и 172.16/12; домашние сети — почти всегда 192.168/16.
    /// Правило грубое, поэтому оно только определяет ПОРЯДОК: если такой адрес окажется
    /// единственным рабочим, мы всё равно предпочтём ему публичный резолвер.
    /// </summary>
    private static bool LooksLikeTunnelAddress(string address)
    {
        if (address.StartsWith("10.", StringComparison.Ordinal)) return true;
        if (!address.StartsWith("172.", StringComparison.Ordinal)) return false;
        var second = address.Split('.').ElementAtOrDefault(1);
        return int.TryParse(second, out var octet) && octet is >= 16 and <= 31;
    }

    /// <summary>Отвечает ли этот DNS-сервер на настоящий запрос. Две секунды на ответ.</summary>
    private static bool Answers(string server)
    {
        try
        {
            using var udp = new System.Net.Sockets.UdpClient();
            udp.Client.ReceiveTimeout = 2000;
            udp.Connect(server, 53);

            // минимальный запрос A-записи для example.com
            var query = new byte[] {
                0x2a, 0x2a, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                7, (byte)'e', (byte)'x', (byte)'a', (byte)'m', (byte)'p', (byte)'l', (byte)'e',
                3, (byte)'c', (byte)'o', (byte)'m', 0x00, 0x00, 0x01, 0x00, 0x01,
            };
            udp.Send(query, query.Length);

            var from = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
            var answer = udp.Receive(ref from);
            if (answer.Length < 12 || answer[0] != 0x2a || answer[1] != 0x2a) return false;
            if ((answer[2] & 0x80) == 0) return false;              // это не ответ
            if ((answer[3] & 0x0F) != 0) return false;              // ответ с ошибкой

            // Мало «ответил» — нужен настоящий адрес в ответе. Виртуальные сетевые карты
            // (Docker, WSL) держат свой DNS, который откликается, но публичных имён не знает.
            // Поймано живьём: такой сервер уехал в конфиг, и машина осталась без имён.
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
        // Сначала DNS того интерфейса, через который машина реально выходит в интернет.
        // У Windows таких интерфейсов много: Docker, WSL, виртуальные сети — у каждого
        // свой DNS, и он даже отвечает, но публичные имена знает не всякий. Поймано живьём:
        // в конфиг уезжал DNS докеровского моста, и с поднятой защитой имена не резолвились.
        // Сначала DNS ФИЗИЧЕСКИХ сетевых карт. На машине запросто работает ещё один VPN
        // (у владельца это был happ-tun), и его DNS живёт только пока жив тот туннель.
        // Наш TUN перекраивает маршруты — чужой резолвер становится недостижим, и машина
        // остаётся без имён. Поймано живьём на Windows.
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
            // берём всё ПОСЛЕ первого двоеточия: у IPv6 адрес сам полон двоеточий,
            // и деление по последнему превращало «fd7a::53» в «53» — а .NET считает
            // «53» адресом 0.0.0.53 и молча пропускает его дальше
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

    /// <summary>curl нужен для проб выхода — HttpClient через локальный вход sing-box глухо таймаутит.</summary>
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

    /// <summary>
    /// Файл есть И его можно запустить.
    ///
    /// Одного File.Exists мало: битая символическая ссылка проходит эту проверку, и продукт
    /// объявлял установленной программу, которой в системе нет. Поймано живьём на macOS —
    /// /usr/local/bin/cursor вёл на давно отмонтированный том установщика.
    /// </summary>
    public static bool IsRunnable(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            var target = RealPath(path);
            if (!File.Exists(target)) return false;          // ссылка в никуда
            if (IsWindows) return true;

    #pragma warning disable CA1416   // сюда не попадаем на Windows: выше стоит ранний выход
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

    /// <summary>
    /// Физический путь, без символических ссылок.
    ///
    /// Это не косметика: ядро macOS отдаёт движку именно физический путь. Приложение,
    /// добавленное как /tmp/app, в системе выглядит как /private/tmp/app, и правило
    /// изоляции по исходному пути не срабатывает МОЛЧА — продукт рапортует «изолировано»,
    /// а трафик идёт мимо. Поймано живьём на macOS.
    /// </summary>
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

    /// <summary>Открыть панель в браузере. Единственное место, где нужен GUI-хост системы.</summary>
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
