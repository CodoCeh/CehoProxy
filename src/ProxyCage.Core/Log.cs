using System.Text;
using System.Text.RegularExpressions;

namespace ProxyCage.Core;

/// <summary>Что показывать из журнала: всё, только наше, только движок или только падения.</summary>
public enum LogView { All, Ours, Engine, Crashes }

public sealed record LogEntry(DateTime When, string Level, string Component, string Message)
{
    public bool IsEngine => Component.Equals(Log.EngineComponent, StringComparison.Ordinal);

    public bool IsCrash => Level.Equals("crash", StringComparison.Ordinal);

    public override string ToString() =>
        $"{When:yyyy-MM-dd HH:mm:ss} {Level,-5} {Component,-6} {Message}";
}

public sealed record CrashRecord(DateTime When, string Context, IReadOnlyList<string> Lines);

/// <summary>
/// Единственный журнал программы. Сюда пишет и сама программа, и движок, и разбор
/// падений: один файл рядом с программой вместо россыпи по системе.
/// </summary>
public static class Log
{
    public const string EngineComponent = "engine";

    private const long MaxBytes = 2 * 1024 * 1024;
    private const string CrashOpen = ">>> падение: ";
    private const string CrashClose = "<<< конец падения";

    private static readonly object Gate = new();
    private static string? _root;
    private static string _component = "chp";

    // Без метки порядка байтов: журнал открывают и обычным «Блокнотом», и в панели.
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    private static readonly Regex LineShape = new(
        @"^(?<t>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) (?<lvl>\S+) +(?<cmp>\S+) +(?<msg>.*)$",
        RegexOptions.Compiled);

    private static readonly Regex AnsiStrip = new(@"\x1b\[[0-9;]*[a-zA-Z]", RegexOptions.Compiled);

    private static readonly Regex EngineLevelRegex = new(
        @"\b(?<lvl>FATAL|PANIC|ERROR|WARN(?:ING)?|INFO|DEBUG|TRACE)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool EchoToConsole { get; set; }

    public static string? Root => _root;

    /// <summary>Файл журнала — он же единственный, который программа ведёт на диске.</summary>
    public static string? FilePath => _root is null ? null : Path.Combine(_root, "cehoproxy.log");

    private static string? RotatedPath => FilePath is null ? null : FilePath + ".1";

    public static void Init(string root, string component)
    {
        lock (Gate)
        {
            _root = root;
            _component = component;
            try { Directory.CreateDirectory(root); } catch { }
        }
        DropLegacyFiles();
        Write("info", _component, $"{component} {Updater.CurrentVersion} на {Os.Describe()}, pid {Environment.ProcessId}");
    }

    /// <summary>
    /// Прошлые версии держали отдельный лог движка и файл на каждое падение.
    /// Теперь всё в одном журнале, а старые файлы только мусорят в папке.
    /// </summary>
    private static void DropLegacyFiles()
    {
        if (_root is null) return;
        try
        {
            foreach (var name in new[] { "sing-box.log", "sing-box.log.1" })
            {
                var path = Path.Combine(_root, name);
                if (File.Exists(path)) File.Delete(path);
            }
            foreach (var crash in Directory.GetFiles(_root, "crash-*.log")) File.Delete(crash);
        }
        catch { }
    }

    public static void Info(string message) => Write("info", _component, message);

    public static void Warn(string message) => Write("warn", _component, message);

    public static void Error(string message) => Write("error", _component, message);

    public static void Error(string message, Exception ex) =>
        Write("error", _component, $"{message}: {ex.GetType().Name}: {ex.Message}");

    /// <summary>Строка от движка: он пишет нам в канал, своего файла у него нет.</summary>
    public static void Engine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        if (IsEngineNoise(line)) return;
        Write(EngineLevel(line), EngineComponent, line.Trim());
    }

    /// <summary>
    /// Cronet при старте naive всегда тычется в DoH Google по IPv6 — на Windows это
    /// «address is not valid», связь при этом живая. Сброс TCP при bounce/выключении
    /// sing-box пишет как ERROR. В журнал это не кладём: человек ищет поломку, а не RST.
    /// </summary>
    public static bool IsEngineNoise(string line)
    {
        var clean = AnsiStrip.Replace(line, "");
        if (clean.Contains("2001:4860:4860::", StringComparison.OrdinalIgnoreCase))
            return true;
        if (clean.Contains("udp", StringComparison.OrdinalIgnoreCase)
            && (clean.Contains("8.8.8.8]:443", StringComparison.OrdinalIgnoreCase)
                || clean.Contains("8.8.4.4]:443", StringComparison.OrdinalIgnoreCase)))
            return true;
        if (clean.Contains("forcibly closed by the remote host", StringComparison.OrdinalIgnoreCase))
            return true;
        if (clean.Contains("aborted by the software in your host machine", StringComparison.OrdinalIgnoreCase))
            return true;
        if (clean.Contains("connection reset by peer", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static string EngineLevel(string line)
    {
        var clean = AnsiStrip.Replace(line, "");
        var m = EngineLevelRegex.Match(clean);
        if (m.Success)
        {
            var lvl = m.Groups["lvl"].Value.ToLowerInvariant();
            return lvl switch
            {
                "fatal" or "panic" or "error" => "error",
                "warn" or "warning" => "warn",
                "debug" or "trace" => "debug",
                _ => "info"
            };
        }
        return "info";
    }

    /// <summary>
    /// Разбор падения — блоком в тот же журнал: сообщения в консоли не увидит никто,
    /// когда программа работает службой или задачей планировщика.
    /// </summary>
    public static void Crash(string context, Exception? ex)
    {
        var lines = new List<string>
        {
            $"версия {Updater.CurrentVersion}, {Os.Describe()}, " +
            $"{_component} pid {Environment.ProcessId}, " +
            $"права: {(Os.IsElevated() ? "администратор" : "обычные")}",
        };

        for (var e = ex; e is not null; e = e.InnerException)
        {
            lines.Add($"{e.GetType().FullName}: {e.Message}");
            foreach (var frame in (e.StackTrace ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                lines.Add("  " + frame.TrimEnd());
        }

        if (ex is null) lines.Add("причина неизвестна");

        lock (Gate)
        {
            Write("crash", _component, CrashOpen + context);
            foreach (var line in lines) Write("crash", _component, line);
            Write("crash", _component, CrashClose);
        }
    }

    private static void Write(string level, string component, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level,-5} {component,-6} {message}";

        if (EchoToConsole)
            try { Console.Error.WriteLine(message); } catch { }

        var path = FilePath;
        if (path is null) return;

        lock (Gate)
        {
            try
            {
                Rotate(path);
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, Utf8);
                writer.WriteLine(line);
            }
            catch { }
        }
    }

    private static void Rotate(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaxBytes) return;

            var previous = RotatedPath!;
            if (File.Exists(previous)) File.Delete(previous);
            File.Move(path, previous);
        }
        catch { }
    }

    /// <summary>Очищает журнал — целиком, вместе с предыдущей частью.</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            try { if (RotatedPath is not null && File.Exists(RotatedPath)) File.Delete(RotatedPath); } catch { }
            try { if (FilePath is not null) File.WriteAllText(FilePath, ""); } catch { }
        }
        Write("info", _component, "журнал очищен");
    }

    public static IReadOnlyList<string> Tail(int lines) => Tail(lines, LogView.All);

    /// <summary>Последние строки журнала нужного вида, как в файле: старые сверху.</summary>
    public static IReadOnlyList<string> Tail(int lines, LogView view) =>
        Entries(lines, view).Select(e => e.ToString()).ToList();

    /// <summary>Те же строки, свежие сверху: так читают журнал в панели, не с конца блока.</summary>
    public static IReadOnlyList<string> TailNewestFirst(int lines, LogView view) =>
        Tail(lines, view).Reverse().ToList();

    public static IReadOnlyList<LogEntry> Entries(int lines, LogView view = LogView.All)
    {
        if (lines <= 0) return Array.Empty<LogEntry>();

        var kept = new Queue<LogEntry>(lines);
        foreach (var entry in ReadAll())
        {
            if (!Matches(entry, view)) continue;
            if (kept.Count == lines) kept.Dequeue();
            kept.Enqueue(entry);
        }
        return kept.ToList();
    }

    private static bool Matches(LogEntry entry, LogView view) => view switch
    {
        LogView.Engine => entry.IsEngine,
        LogView.Ours => !entry.IsEngine,
        LogView.Crashes => entry.IsCrash,
        _ => true,
    };

    /// <summary>Записанные падения, свежие сверху: блок от начала до конца.</summary>
    public static IReadOnlyList<CrashRecord> Crashes(int max = 10)
    {
        var found = new List<CrashRecord>();
        DateTime when = default;
        string? context = null;
        var lines = new List<string>();

        foreach (var entry in ReadAll())
        {
            if (!entry.IsCrash) continue;

            if (entry.Message.StartsWith(CrashOpen, StringComparison.Ordinal))
            {
                when = entry.When;
                context = entry.Message[CrashOpen.Length..];
                lines = new List<string>();
                continue;
            }

            if (context is null) continue;

            if (entry.Message.StartsWith(CrashClose, StringComparison.Ordinal))
            {
                found.Add(new CrashRecord(when, context, lines));
                context = null;
                continue;
            }

            lines.Add(entry.Message);
        }

        // Незакрытый блок: программу убили посреди записи — показать всё равно надо.
        if (context is not null) found.Add(new CrashRecord(when, context, lines));

        found.Reverse();
        return found.Count > max ? found.Take(max).ToList() : found;
    }

    private static IEnumerable<LogEntry> ReadAll()
    {
        foreach (var path in new[] { RotatedPath, FilePath })
        {
            if (path is null || !File.Exists(path)) continue;

            List<string> lines;
            try { lines = ReadLines(path); }
            catch { continue; }

            foreach (var line in lines)
            {
                var m = LineShape.Match(line);
                if (!m.Success) continue;
                if (!DateTime.TryParse(m.Groups["t"].Value, out var when)) continue;

                yield return new LogEntry(
                    when, m.Groups["lvl"].Value, m.Groups["cmp"].Value, m.Groups["msg"].Value);
            }
        }
    }

    private static List<string> ReadLines(string path)
    {
        var lines = new List<string>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line) lines.Add(line);
        return lines;
    }
}
