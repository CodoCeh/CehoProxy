namespace ProxyCage.Core;

public static class CommandName
{
    private static readonly char[] PathOrShellChars = ['/', '\\', ':', '<', '>', '"', '|', '?', '*'];

    public static bool IsSafe(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name is not "." and not ".." &&
        !name.Any(char.IsWhiteSpace) &&
        !name.Any(char.IsControl) &&
        name.IndexOfAny(PathOrShellChars) < 0 &&
        !Path.IsPathRooted(name);

    public static string Canonical(string name, bool windows)
    {
        if (!windows) return name;
        var extension = Path.GetExtension(name);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(name)
            : name;
    }

    public static IReadOnlyList<string> ExecutableCandidates(string name, bool windows)
    {
        var canonical = Canonical(name, windows);
        return windows
            ? new[] { canonical + ".exe", canonical + ".cmd", canonical + ".bat", canonical }
            : new[] { canonical };
    }

    public static string WrapperFileName(string name, bool windows) =>
        Canonical(name, windows) + (windows ? ".cmd" : "");
}
