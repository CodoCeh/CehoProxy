namespace ProxyCage.Core;

/// <summary>
/// Поиск установленных ИИ-инструментов, чтобы не заставлять человека вспоминать пути.
///
/// Ищем только по известным местам и в PATH: обход диска на терминальном сервере занял бы
/// минуты и ничего не дал бы. Найденное показываем с честной пометкой, можно ли его вообще
/// изолировать — см. <see cref="ToolKind"/>.
/// </summary>
public static class AiTools
{
    public enum ToolKind
    {
        /// <summary>Пакет .app или папка программы: изолируется целиком, всё честно.</summary>
        Bundle,

        /// <summary>Обычный исполняемый файл: правило строится по нему.</summary>
        Native,

        /// <summary>
        /// Скрипт под интерпретатором (обычно node). Система видит процесс ИНТЕРПРЕТАТОРА,
        /// а не скрипта, поэтому правило по пути скрипта не сработает — молча, что хуже всего.
        /// Проверено: у запущенного через `#!/usr/bin/env node` процесса путь — сам node.
        /// </summary>
        Script,
    }

    public sealed record Found(string Name, string Path, ToolKind Kind, string? Interpreter);

    private sealed record Candidate(string Name, string[] Paths, string[] Commands);

    private static IEnumerable<Candidate> Catalog()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        switch (Os.Kind)
        {
            case OsKind.Mac:
                yield return new("Claude", new[] { "/Applications/Claude.app" }, new[] { "claude" });
                yield return new("Codex", new[] { "/Applications/Codex.app" }, new[] { "codex" });
                yield return new("Cursor", new[] { "/Applications/Cursor.app" }, new[] { "cursor" });
                yield return new("Gemini", new[] { "/Applications/Gemini.app" }, new[] { "gemini" });
                // именно папка инструмента: bin/grok — ссылка на файл с номером версии в имени,
                // и после обновления правило по нему молча перестало бы совпадать
                yield return new("Grok", new[] { "/Applications/Grok.app", $"{home}/.grok" }, new[] { "grok" });
                yield return new("Antigravity", new[]
                {
                    "/Applications/Antigravity.app", "/Applications/Antigravity IDE.app",
                }, Array.Empty<string>());
                yield return new("ChatGPT", new[] { "/Applications/ChatGPT.app" }, Array.Empty<string>());
                break;

            case OsKind.Windows:
                yield return new("Claude", new[]
                {
                    Path.Combine(local, "AnthropicClaude"),
                    Path.Combine(local, "Programs", "claude"),
                }, new[] { "claude" });
                yield return new("Codex", new[]
                {
                    Path.Combine(programFiles, "WindowsApps", "OpenAI.Codex*"),
                    Path.Combine(local, "Programs", "codex"),
                }, new[] { "codex" });
                yield return new("Cursor", new[] { Path.Combine(local, "Programs", "cursor") }, new[] { "cursor" });
                yield return new("Antigravity", new[] { Path.Combine(local, "Programs", "Antigravity") },
                    Array.Empty<string>());
                yield return new("Gemini", Array.Empty<string>(), new[] { "gemini" });
                yield return new("Grok", Array.Empty<string>(), new[] { "grok" });
                yield return new("ChatGPT", new[] { Path.Combine(local, "Programs", "ChatGPT") },
                    Array.Empty<string>());
                break;

            default:
                yield return new("Claude", new[] { "/opt/claude" }, new[] { "claude" });
                yield return new("Codex", new[] { "/opt/codex" }, new[] { "codex" });
                yield return new("Cursor", new[] { "/opt/cursor", "/usr/share/cursor" }, new[] { "cursor" });
                yield return new("Antigravity", new[] { "/opt/antigravity", "/usr/share/antigravity" },
                    Array.Empty<string>());
                yield return new("Gemini", Array.Empty<string>(), new[] { "gemini" });
                yield return new("Grok", new[] { $"{home}/.grok" }, new[] { "grok" });
                break;
        }
    }

    public static IReadOnlyList<Found> Detect()
    {
        var found = new List<Found>();

        foreach (var tool in Catalog())
        {
            var hit = tool.Paths.Select(Expand).FirstOrDefault(p => p is not null)
                      ?? tool.Commands.Select(Os.FindOnPath).FirstOrDefault(p => p is not null);
            if (hit is null) continue;

            var real = Os.RealPath(hit);
            found.Add(new Found(tool.Name, real, KindOf(real), InterpreterOf(real)));
        }

        return found;
    }

    /// <summary>Путь с хвостовой звёздочкой означает «папка с меняющимся суффиксом» (MSIX-пакеты).</summary>
    private static string? Expand(string pattern)
    {
        if (!pattern.EndsWith('*'))
            return Directory.Exists(pattern) || Os.IsRunnable(pattern) ? pattern : null;

        var dir = Path.GetDirectoryName(pattern);
        var prefix = Path.GetFileName(pattern).TrimEnd('*');
        if (string.IsNullOrEmpty(dir)) return null;

        try
        {
            return Directory.EnumerateDirectories(dir, prefix + "*")
                .OrderByDescending(d => d, StringComparer.Ordinal)
                .FirstOrDefault();
        }
        catch
        {
            return null;   // WindowsApps закрыт от чтения — это норма
        }
    }

    private static ToolKind KindOf(string path)
    {
        if (Directory.Exists(path)) return ToolKind.Bundle;
        return InterpreterOf(path) is null ? ToolKind.Native : ToolKind.Script;
    }

    /// <summary>Первая строка «#!…» — значит запускать будет интерпретатор, и процессом станет он.</summary>
    private static string? InterpreterOf(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> head = stackalloc byte[128];
            var read = stream.Read(head);
            if (read < 3 || head[0] != '#' || head[1] != '!') return null;

            var line = System.Text.Encoding.UTF8.GetString(head[2..read]);
            var end = line.IndexOfAny(new[] { '\n', '\r' });
            if (end >= 0) line = line[..end];

            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;

            // «/usr/bin/env node» — настоящий интерпретатор во втором слове
            var name = Path.GetFileName(parts[0]) == "env" && parts.Length > 1 ? parts[1] : parts[0];
            return Os.FindOnPath(Path.GetFileName(name)) ?? name;
        }
        catch
        {
            return null;
        }
    }
}
