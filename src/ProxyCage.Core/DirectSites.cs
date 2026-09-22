using System.Globalization;
using System.Net;

namespace ProxyCage.Core;

public static class DirectSites
{
    public static string? Normalize(string? raw)
    {
        var text = (raw ?? "").Trim().Trim('"', '\'');
        if (text.Length == 0) return null;

        var withScheme = text.Contains("://", StringComparison.Ordinal) ? text : "https://" + text;
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var uri)) return null;
        if (uri.Host.Length == 0) return null;

        var ascii = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (ascii.Length == 0 || ascii.Contains(' ')) return null;
        if (ascii.StartsWith("www.", StringComparison.Ordinal) && ascii.IndexOf('.', 4) > 0)
            ascii = ascii[4..];
        if (ascii.Length == 0 || !ascii.Contains('.')) return null;
        if (IPAddress.TryParse(ascii, out _)) return null;

        try
        {
            return new IdnMapping().GetUnicode(ascii);
        }
        catch (ArgumentException)
        {
            return ascii;
        }
    }

    public static string ToAscii(string host)
    {
        try
        {
            return new IdnMapping().GetAscii(host).TrimEnd('.').ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return host.TrimEnd('.').ToLowerInvariant();
        }
    }
}
