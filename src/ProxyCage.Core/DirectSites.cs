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

    public static string? NormalizeCountry(string? raw)
    {
        var text = (raw ?? "").Trim();
        if (text.Length != 2) return null;
        if (!char.IsLetter(text[0]) || !char.IsLetter(text[1])) return null;
        return text.ToUpperInvariant();
    }

    public static readonly string[] TunnelSites =
    [
        "youtube.com", "google.com", "gmail.com", "instagram.com", "facebook.com",
        "x.com", "twitter.com", "chatgpt.com", "openai.com", "claude.ai",
        "discord.com", "netflix.com", "spotify.com", "reddit.com", "tiktok.com",
        "twitch.tv", "linkedin.com", "wikipedia.org",
    ];

    public static readonly string[] RussianSites =
    [
        "yandex.ru", "ya.ru", "vk.com", "mail.ru", "ok.ru", "dzen.ru",
        "gosuslugi.ru", "nalog.gov.ru", "sberbank.ru", "sber.ru", "tinkoff.ru",
        "tbank.ru", "vtb.ru", "alfabank.ru", "ozon.ru", "wildberries.ru",
        "avito.ru", "kinopoisk.ru", "rutube.ru", "2gis.ru", "hh.ru", "rzd.ru",
    ];

    public static IReadOnlyList<string> Preset(bool throughTunnel) =>
        throughTunnel ? TunnelSites : RussianSites;

    public static List<string> NewHosts(IEnumerable<string> listed, IEnumerable<string> incoming)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var site in listed)
        {
            var host = Normalize(site) ?? site.Trim();
            if (host.Length > 0) known.Add(host);
        }

        var fresh = new List<string>();
        foreach (var raw in incoming)
        {
            var host = Normalize(raw);
            if (host is null || !known.Add(host)) continue;
            fresh.Add(host);
        }
        return fresh;
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
