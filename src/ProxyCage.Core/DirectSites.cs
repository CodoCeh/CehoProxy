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
        "gosuslugi.ru", "госуслуги.рф", "nalog.gov.ru", "nalog.ru", "mos.ru", "gov.ru", "pfr.gov.ru", "sfr.gov.ru", "fssp.gov.ru", "rosreestr.gov.ru", "rosreestr.ru", "mvd.ru", "мвд.рф", "kremlin.ru", "президент.рф", "government.ru", "правительство.рф", "cikrf.ru", "duma.gov.ru", "rkn.gov.ru", "goskey.ru", "mil.ru", "customs.gov.ru", "zakupki.gov.ru", "torgi.gov.ru", "pochta.ru", "russianpost.ru", "cbr.ru", "moex.com", "consultant.ru", "garant.ru",
        "max.ru", "web.max.ru", "oneme.ru", "vk.com", "vk.ru", "vkontakte.ru", "vk-cdn.net", "userapi.com", "vkuseraudio.net", "vkuservideo.net", "vkplay.ru", "vkplay.live", "vkvideo.ru", "ok.ru", "mycdn.me", "okcdn.ru", "mail.ru", "my.mail.ru", "imgsmail.ru", "tamtam.chat", "pikabu.ru", "habr.com", "vc.ru", "dtf.ru",
        "yandex.ru", "ya.ru", "yandex.net", "yandex.by", "yandex.kz", "yandex.com", "yastatic.net", "dzen.ru", "kinopoisk.ru", "2gis.ru", "2gis.com",
        "sberbank.ru", "sber.ru", "sberbank.com", "sbrf.ru", "sbermarket.ru", "megamarket.ru", "sbermegamarket.ru", "zvuk.com", "tinkoff.ru", "tbank.ru", "vtb.ru", "alfabank.ru", "gazprombank.ru", "gpb.ru", "raiffeisen.ru", "rshb.ru", "psbank.ru", "sovcombank.ru", "mkb.ru", "open.ru", "rosbank.ru", "nspk.ru", "mironline.ru", "qiwi.com", "yoomoney.ru", "yookassa.ru", "cloudpayments.ru", "robokassa.ru", "banki.ru", "sravni.ru",
        "ozon.ru", "wildberries.ru", "wb.ru", "avito.ru", "youla.ru", "lamoda.ru", "dns-shop.ru", "citilink.ru", "mvideo.ru", "eldorado.ru", "aliexpress.ru", "detmir.ru", "leroymerlin.ru", "letu.ru", "goldapple.ru", "vkusvill.ru", "perekrestok.ru", "magnit.ru", "5ka.ru", "samokat.ru", "delivery-club.ru", "cdek.ru", "boxberry.ru", "5post.ru",
        "playerok.com", "funpay.com", "plati.market", "plati.ru", "digiseller.ru", "ggsel.net", "zaka-zaka.com", "lolz.guru", "lzt.market", "my.games", "4game.com", "lesta.ru", "rustore.ru",
        "rutube.ru", "ivi.ru", "okko.tv", "kion.ru", "wink.ru", "premier.one", "more.tv", "start.ru", "smotrim.ru", "1tv.ru", "ntv.ru", "vesti.ru", "ria.ru", "tass.ru", "rbc.ru", "kommersant.ru", "rg.ru", "gazeta.ru", "lenta.ru", "iz.ru", "mk.ru", "kp.ru", "aif.ru",
        "rzd.ru", "aeroflot.ru", "s7.ru", "pobeda.aero", "utair.ru", "tutu.ru", "ostrovok.ru", "sutochno.ru", "aviasales.ru",
        "mts.ru", "beeline.ru", "megafon.ru", "tele2.ru", "rostelecom.ru", "rt.ru", "yota.ru", "dom.ru",
        "hh.ru", "superjob.ru", "rabota.ru", "gb.ru", "skillbox.ru", "netology.ru", "stepik.org", "uchi.ru", "sdamgia.ru", "1c.ru", "sbis.ru", "kontur.ru", "diadoc.ru",
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
