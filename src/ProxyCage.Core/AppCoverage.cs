using System.Text.RegularExpressions;

namespace ProxyCage.Core;

/// <summary>
/// Покрывает ли уже добавленная программа путь процесса — по тем же regex, что попадают в singbox.json.
/// </summary>
public static class AppCoverage
{
    /// <summary>Returns the existing enabled routing rule, without claiming exact app identity.</summary>
    public static AppEntry? FindCoveringApp(IEnumerable<AppEntry> apps, string path)
    {
        var full = AppIdentity.Normalize(path);
        var probes = Directory.Exists(full)
            ? new[] { full, full.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "__ceho_coverage_probe__" }
            : new[] { full };
        return apps.FirstOrDefault(app => app.Enabled && !string.IsNullOrWhiteSpace(app.Folder)
            && AppDetector.ToRegexes(app).Any(rx => probes.Any(probe =>
                Regex.IsMatch(probe, rx, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))));
    }

    /// <summary>Forms address the stored Folder key even when two rule shapes are not equivalent.</summary>
    public static AppEntry? FindStoredRule(IEnumerable<AppEntry> apps, AppDetector.Detection detected) =>
        apps.FirstOrDefault(app => AppIdentity.SameConfiguredPath(app.Folder, detected.Folder));

    public static AppEntry? FindEquivalentRule(IEnumerable<AppEntry> apps, AppDetector.Detection detected)
    {
        // Literal configured paths become engine regexes. Filesystem identity alone cannot
        // establish equivalent coverage (for example /var versus /private/var on macOS).
        // The primary expression encodes folder/file and version-agnostic scope; additional
        // app-family expressions do not invalidate an already identical primary rule.
        var primary = AppDetector.ToRegex(new AppEntry
        {
            Name = detected.Name, Folder = detected.Folder,
            SingleFile = detected.SingleFile, VersionAgnostic = detected.VersionAgnostic,
        });
        return apps.FirstOrDefault(app =>
        {
            var existing = AppDetector.ToRegex(app);
            var comparison = primary.StartsWith("(?i)", StringComparison.Ordinal)
                && existing.StartsWith("(?i)", StringComparison.Ordinal)
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return existing.Equals(primary, comparison);
        });
    }

    public static bool IsPathCovered(CehoConfig cfg, string processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath)) return false;

        foreach (var app in cfg.Apps.Where(a => a.Enabled && !string.IsNullOrWhiteSpace(a.Folder)))
        foreach (var rx in AppDetector.ToRegexes(app))
        {
            if (Regex.IsMatch(processPath, rx, RegexOptions.CultureInvariant))
                return true;
        }
        return false;
    }

    public static bool IsEntryCovered(CehoConfig cfg, string entryPath) =>
        IsPathCovered(cfg, entryPath)
        || IsPathCovered(cfg, entryPath.TrimEnd('\\', '/') + Path.DirectorySeparatorChar + "x");

    public static bool IsToolCovered(CehoConfig cfg, AiTools.Found tool)
    {
        if (IsPathCovered(cfg, tool.Path)) return true;

        foreach (var probe in ProbeExecutables(tool).Concat(SyntheticProbes(tool)))
        {
            if (IsPathCovered(cfg, probe)) return true;
        }

        return cfg.Apps.Any(a => a.Enabled && (
            AppIdentity.SameConfiguredPath(a.Folder, tool.Path) ||
            tool.Path.StartsWith(a.Folder.TrimEnd('\\', '/') + Path.DirectorySeparatorChar,
                AppIdentity.Comparison())));
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
