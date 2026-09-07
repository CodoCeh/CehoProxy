using System.Text.RegularExpressions;

namespace ProxyCage.Core;

/// <summary>
/// Покрывает ли уже добавленная программа путь процесса — по тем же regex, что попадают в singbox.json.
/// </summary>
public static class AppCoverage
{
    public static bool IsPathCovered(CehoConfig cfg, string processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath)) return false;

        foreach (var app in cfg.Apps.Where(a => a.Enabled && !string.IsNullOrWhiteSpace(a.Folder)))
        foreach (var rx in AppDetector.ToRegexes(app))
        {
            if (Regex.IsMatch(processPath, rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return true;
        }
        return false;
    }

    public static bool IsToolCovered(CehoConfig cfg, AiTools.Found tool)
    {
        if (IsPathCovered(cfg, tool.Path)) return true;

        foreach (var probe in ProbeExecutables(tool).Concat(SyntheticProbes(tool)))
        {
            if (IsPathCovered(cfg, probe)) return true;
        }

        return cfg.Apps.Any(a => a.Enabled && (
            a.Folder.Equals(tool.Path, StringComparison.OrdinalIgnoreCase) ||
            tool.Path.StartsWith(a.Folder.TrimEnd('\\', '/') + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)));
    }

    private static IEnumerable<string> SyntheticProbes(AiTools.Found tool)
    {
        if (tool.Name.StartsWith("Codex", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(tool.Path, "bin", "0000000000000000", "codex.exe");
            yield return Path.Combine(tool.Path, "codex.exe");
        }
        else if (tool.Name.StartsWith("Cursor", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(tool.Path, "Cursor.exe");
            yield return Path.Combine(tool.Path, ".cursor", "server", "bin", "node.exe");
        }
    }

    private static IEnumerable<string> ProbeExecutables(AiTools.Found tool)
    {
        if (!Directory.Exists(tool.Path)) yield break;

        foreach (var name in tool.Name.StartsWith("Codex", StringComparison.OrdinalIgnoreCase)
                     ? new[] { "codex.exe", "ChatGPT.exe" }
                     : tool.Name.StartsWith("Cursor", StringComparison.OrdinalIgnoreCase)
                         ? new[] { "Cursor.exe" }
                         : Array.Empty<string>())
        {
            var probe = Path.Combine(tool.Path, name);
            if (File.Exists(probe)) yield return probe;
        }

        if (tool.Name.StartsWith("Codex", StringComparison.OrdinalIgnoreCase)
            && Os.IsWindows
            && tool.Path.Contains(@"OpenAI\Codex", StringComparison.OrdinalIgnoreCase))
        {
            string[] extras;
            try
            {
                extras = Directory.EnumerateFiles(tool.Path, "codex.exe", SearchOption.AllDirectories).ToArray();
            }
            catch
            {
                extras = Array.Empty<string>();
            }

            foreach (var exe in extras)
                yield return exe;
        }
    }
}
