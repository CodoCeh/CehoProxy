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
}
