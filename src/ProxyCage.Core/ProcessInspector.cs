using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ProxyCage.Core;

/// <summary>
/// Чем доказывается изоляция: какие процессы принадлежат приложению и с какого локального
/// адреса они реально соединяются. У завёрнутых в туннель адрес из подсети TUN,
/// у утекающих мимо — адрес сетевой карты. Способ добыть это у каждой системы свой.
/// </summary>
public static class ProcessInspector
{
    public static IReadOnlySet<int> PidsOf(AppEntry app)
    {
        var rx = new Regex(AppDetector.ToRegex(app),
            Os.IsLinux ? RegexOptions.None : RegexOptions.IgnoreCase);

        var pids = new HashSet<int>();
        foreach (var (pid, path) in RunningExecutables())
            if (rx.IsMatch(path)) pids.Add(pid);
        return pids;
    }

    private static IEnumerable<(int Pid, string Path)> RunningExecutables() => Os.Kind switch
    {
        OsKind.Windows => WindowsExecutables(),
        OsKind.Linux => LinuxExecutables(),
        _ => MacExecutables(),
    };

    private static IEnumerable<(int, string)> WindowsExecutables()
    {
        foreach (var p in Process.GetProcesses())
        {
            // идентификатор забираем ДО освобождения объекта: после Dispose обращение
            // к нему падает с «No process is associated with this object», и проверка
            // изоляции разваливалась целиком. Поймано живьём на Windows
            int id;
            string? path = null;
            try
            {
                id = p.Id;
                path = p.MainModule?.FileName;
            }
            catch { continue; }          // чужие процессы без доступа к модулю — не наши
            finally { p.Dispose(); }

            if (path is not null) yield return (id, path);
        }
    }

    private static IEnumerable<(int, string)> LinuxExecutables()
    {
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            var name = Path.GetFileName(dir);
            if (!int.TryParse(name, out var pid)) continue;
            string? target = null;
            // /proc/<pid>/exe читается напрямую, без разбора карт памяти:
            // Process.MainModule на Linux ходит по /proc/<pid>/maps и на чужих процессах падает
            try { target = File.ResolveLinkTarget(Path.Combine(dir, "exe"), true)?.FullName; }
            catch { }
            if (target is not null) yield return (pid, target);
        }
    }

    private static IEnumerable<(int, string)> MacExecutables()
    {
        var (code, output) = Os.Run("ps", "-axo pid=,comm=", 15000);
        if (code != 0) yield break;
        foreach (var line in output.Split('\n'))
        {
            var t = line.TrimStart();
            var sp = t.IndexOf(' ');
            if (sp <= 0 || !int.TryParse(t[..sp], out var pid)) continue;
            var path = t[(sp + 1)..].Trim();
            // ps показывает путь так, как программу запустили; правило же построено
            // по физическому пути — без приведения проверка изоляции разошлась бы с движком
            if (path.Length > 0) yield return (pid, Os.RealPath(path));
        }
    }

    /// <summary>Локальные адреса установленных TCP-соединений указанных процессов.</summary>
    public static IEnumerable<string> LocalAddressesOf(IReadOnlySet<int> pids) => Os.Kind switch
    {
        OsKind.Windows => WindowsLocalAddresses(pids),
        OsKind.Linux => LinuxLocalAddresses(pids),
        _ => MacLocalAddresses(pids),
    };

    private static IEnumerable<string> WindowsLocalAddresses(IReadOnlySet<int> pids)
    {
        var (_, text) = Os.Run("netstat", "-ano -p TCP", 15000);
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            // TCP  <local>  <remote>  ESTABLISHED  <pid>
            if (parts.Length < 5 || !parts[0].Equals("TCP", StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(parts[^1], out var pid) || !pids.Contains(pid)) continue;

            // Считаем только ЖИВЫЕ соединения. Слушающие сокеты и закрывающиеся хвосты
            // (FIN_WAIT, TIME_WAIT) идут с прежнего адреса и выглядели как утечка: продукт
            // показывал «ТЕЧЁТ» при исправной изоляции сразу после включения защиты.
            // Названия состояний TCP Windows не переводит, опираться на них можно.
            if (!parts[3].Equals("ESTABLISHED", StringComparison.OrdinalIgnoreCase)) continue;

            yield return StripPort(parts[1]);
        }
    }

    private static IEnumerable<string> MacLocalAddresses(IReadOnlySet<int> pids)
    {
        if (pids.Count == 0) yield break;
        var (code, output) = Os.Run("lsof",
            $"-nP -iTCP -sTCP:ESTABLISHED -a -p {string.Join(',', pids)}", 20000);
        if (code != 0) yield break;

        foreach (var line in output.Split('\n'))
        {
            var arrow = line.IndexOf("->", StringComparison.Ordinal);
            if (arrow < 0) continue;
            var lastSpace = line.LastIndexOf(' ', arrow);
            if (lastSpace < 0) continue;
            yield return StripPort(line[(lastSpace + 1)..arrow]);
        }
    }

    private static IEnumerable<string> LinuxLocalAddresses(IReadOnlySet<int> pids)
    {
        var inodes = SocketInodesOf(pids);
        if (inodes.Count == 0) yield break;

        foreach (var file in new[] { "/proc/net/tcp", "/proc/net/tcp6" })
        {
            string[] lines;
            try { lines = File.ReadAllLines(file); } catch { continue; }

            foreach (var line in lines.Skip(1))
            {
                var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                // sl local_address rem_address st tx_queue rx_queue tr tm->when retrnsmt uid timeout inode
                if (f.Length < 10) continue;
                if (f[3] != "01") continue;                 // 01 = ESTABLISHED
                if (!inodes.Contains(f[9])) continue;
                if (ParseHexAddress(f[1]) is { } addr) yield return addr;
            }
        }
    }

    /// <summary>
    /// В /proc/net/tcp нет pid, есть inode сокета. Обратную связь даёт /proc/&lt;pid&gt;/fd:
    /// каждый сокет там — симлинк вида socket:[12345]. Обходим только процессы приложения.
    /// </summary>
    private static HashSet<string> SocketInodesOf(IReadOnlySet<int> pids)
    {
        var inodes = new HashSet<string>();
        foreach (var pid in pids)
        {
            IEnumerable<string> fds;
            try { fds = Directory.EnumerateFiles($"/proc/{pid}/fd"); }
            catch { continue; }

            foreach (var fd in fds)
            {
                try
                {
                    var target = File.ResolveLinkTarget(fd, false)?.Name;
                    if (target is null || !target.StartsWith("socket:[", StringComparison.Ordinal)) continue;
                    inodes.Add(target[8..^1]);
                }
                catch { }
            }
        }
        return inodes;
    }

    /// <summary>0100007F:1F90 → 127.0.0.1 (little-endian по 4 байта, и для IPv6 тоже).</summary>
    private static string? ParseHexAddress(string field)
    {
        var colon = field.IndexOf(':');
        var hex = colon > 0 ? field[..colon] : field;
        if (hex.Length is not (8 or 32)) return null;

        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            if (!byte.TryParse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, null, out bytes[i]))
                return null;

        for (var i = 0; i < bytes.Length; i += 4) Array.Reverse(bytes, i, 4);

        try { return new System.Net.IPAddress(bytes).ToString(); }
        catch { return null; }
    }

    private static string StripPort(string endpoint)
    {
        var colon = endpoint.LastIndexOf(':');
        return colon > 0 ? endpoint[..colon] : endpoint;
    }
}
