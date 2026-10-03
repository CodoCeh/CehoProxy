using System.Text;

namespace ProxyCage.Core;

/// <summary>Positive ownership requires an engine executable and its exact config argument.</summary>
public static class OwnedEngineProcess
{
    public static bool Matches(string processName, IReadOnlyList<string> arguments,
        string runtimeConfigPath, bool windows)
    {
        var comparison = windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        static string Name(string path) => path.Replace('\\', '/').Split('/').Last();
        var name = Name(processName);
        var known = windows ? new[] { "ceho-engine.exe", "sing-box.exe" } : new[] { "ceho-engine", "sing-box" };
        if (!known.Any(n => n.Equals(name, comparison)) || arguments.Count < 4
            || !Name(arguments[0]).Equals(name, comparison) || arguments[1] != "run") return false;
        var configs = new List<string>();
        for (var i = 2; i < arguments.Count; i++)
        {
            if (arguments[i] is "-c" or "--config")
            {
                if (++i >= arguments.Count) return false;
                configs.Add(arguments[i]);
            }
            else if (arguments[i].StartsWith("--config=", StringComparison.Ordinal))
                configs.Add(arguments[i][9..]);
            // Additional directories/configs can merge a different owner's rules.
            else if (arguments[i] is "-C" or "--config-directory"
                     || arguments[i].StartsWith("--config-directory=", StringComparison.Ordinal)) return false;
        }
        return configs.Count == 1 && configs[0].Equals(runtimeConfigPath, comparison);
    }

    // Windows quotes emitted by ProcessStartInfo and conservative ps fallback.
    // Unknown/unbalanced command lines are refused, never matched by substring.
    public static IReadOnlyList<string> Arguments(string commandLine)
    {
        var result = new List<string>();
        var word = new StringBuilder();
        var quoted = false;
        foreach (var c in commandLine)
        {
            if (c == '"') { quoted = !quoted; continue; }
            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (word.Length > 0) { result.Add(word.ToString()); word.Clear(); }
            }
            else word.Append(c);
        }
        if (quoted) return Array.Empty<string>();
        if (word.Length > 0) result.Add(word.ToString());
        return result;
    }
}
