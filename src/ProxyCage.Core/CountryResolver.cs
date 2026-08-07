using System.Globalization;

namespace ProxyCage.Core;

/// <summary>
/// Определяет страну ноды по её подписи и показывает название на языке интерфейса.
///
/// Порядок распознавания важен и проверен на живых подписках:
/// 1. флаг-эмодзи (🇳🇱 → NL) — самый надёжный признак, его ставит большинство провайдеров;
/// 2. название страны — по-русски, по-английски или на языке самой страны;
/// 3. код страны отдельным словом («DE-01», «[NL] node2», «US | Dallas»).
///
/// Названия сверяются от ДЛИННОГО к короткому и только с начала слова. Без этих двух
/// правил «Ukraine» опознавался как Великобритания (из-за подстроки «uk»), «Nigeria» —
/// как Нигер, а «Bukarest» и «Fukuoka» — снова как Великобритания. Всё поймано живьём
/// на настоящих подписях нод.
/// </summary>
public static class CountryResolver
{
    /// <summary>
    /// Русские названия стран. Английские и местные названия платформа знает сама,
    /// русских у неё нет — поэтому таблица здесь. Список покрывает то, что встречается
    /// у VPN-провайдеров; для остальных кодов останется английское название.
    /// </summary>
    private static readonly (string Code, string Ru)[] RussianNames =
    {
        ("AE", "ОАЭ"), ("AL", "Албания"), ("AM", "Армения"), ("AR", "Аргентина"),
        ("AT", "Австрия"), ("AU", "Австралия"), ("AZ", "Азербайджан"),
        ("BA", "Босния и Герцеговина"), ("BD", "Бангладеш"), ("BE", "Бельгия"),
        ("BG", "Болгария"), ("BH", "Бахрейн"), ("BR", "Бразилия"), ("BY", "Беларусь"),
        ("CA", "Канада"), ("CH", "Швейцария"), ("CL", "Чили"), ("CN", "Китай"),
        ("CO", "Колумбия"), ("CR", "Коста-Рика"), ("CY", "Кипр"), ("CZ", "Чехия"),
        ("DE", "Германия"), ("DK", "Дания"), ("DZ", "Алжир"),
        ("EC", "Эквадор"), ("EE", "Эстония"), ("EG", "Египет"), ("ES", "Испания"),
        ("FI", "Финляндия"), ("FR", "Франция"),
        ("GB", "Великобритания"), ("GE", "Грузия"), ("GR", "Греция"),
        ("HK", "Гонконг"), ("HR", "Хорватия"), ("HU", "Венгрия"),
        ("ID", "Индонезия"), ("IE", "Ирландия"), ("IL", "Израиль"), ("IN", "Индия"),
        ("IQ", "Ирак"), ("IR", "Иран"), ("IS", "Исландия"), ("IT", "Италия"),
        ("JO", "Иордания"), ("JP", "Япония"),
        ("KE", "Кения"), ("KG", "Киргизия"), ("KH", "Камбоджа"), ("KR", "Южная Корея"),
        ("KW", "Кувейт"), ("KZ", "Казахстан"),
        ("LB", "Ливан"), ("LT", "Литва"), ("LU", "Люксембург"), ("LV", "Латвия"),
        ("MA", "Марокко"), ("MD", "Молдавия"), ("ME", "Черногория"), ("MK", "Северная Македония"),
        ("MN", "Монголия"), ("MT", "Мальта"), ("MX", "Мексика"), ("MY", "Малайзия"),
        ("NG", "Нигерия"), ("NL", "Нидерланды"), ("NO", "Норвегия"), ("NP", "Непал"),
        ("NZ", "Новая Зеландия"),
        ("OM", "Оман"),
        ("PA", "Панама"), ("PE", "Перу"), ("PH", "Филиппины"), ("PK", "Пакистан"),
        ("PL", "Польша"), ("PT", "Португалия"), ("PY", "Парагвай"),
        ("QA", "Катар"),
        ("RO", "Румыния"), ("RS", "Сербия"), ("RU", "Россия"),
        ("SA", "Саудовская Аравия"), ("SE", "Швеция"), ("SG", "Сингапур"), ("SI", "Словения"),
        ("SK", "Словакия"),
        ("TH", "Таиланд"), ("TJ", "Таджикистан"), ("TM", "Туркмения"), ("TR", "Турция"),
        ("TW", "Тайвань"),
        ("UA", "Украина"), ("US", "США"), ("UY", "Уругвай"), ("UZ", "Узбекистан"),
        ("VE", "Венесуэла"), ("VN", "Вьетнам"),
        ("ZA", "ЮАР"),
    };

    /// <summary>
    /// Что провайдеры пишут вместо названия страны: города, разговорные и старые формы.
    /// Города здесь потому, что «Frankfurt-01» или «Amsterdam node» — обычная подпись ноды.
    /// </summary>
    private static readonly (string Needle, string Code)[] Aliases =
    {
        ("сша", "US"), ("соединённые штаты", "US"), ("соединенные штаты", "US"),
        ("америка", "US"), ("usa", "US"), ("dallas", "US"), ("new york", "US"),
        ("los angeles", "US"), ("miami", "US"), ("seattle", "US"), ("ashburn", "US"),
        ("оаэ", "AE"), ("uae", "AE"), ("дубай", "AE"), ("dubai", "AE"),
        ("юар", "ZA"), ("южная африка", "ZA"),
        ("англия", "GB"), ("британия", "GB"), ("britain", "GB"), ("england", "GB"),
        ("лондон", "GB"), ("london", "GB"),
        ("голландия", "NL"), ("holland", "NL"), ("амстердам", "NL"), ("amsterdam", "NL"),
        ("франкфурт", "DE"), ("frankfurt", "DE"), ("берлин", "DE"), ("berlin", "DE"),
        ("москва", "RU"), ("moscow", "RU"), ("питер", "RU"), ("санкт-петербург", "RU"),
        ("хельсинки", "FI"), ("helsinki", "FI"),
        ("варшава", "PL"), ("warsaw", "PL"), ("warszawa", "PL"),
        ("стамбул", "TR"), ("istanbul", "TR"),
        ("париж", "FR"), ("paris", "FR"),
        ("токио", "JP"), ("tokyo", "JP"), ("osaka", "JP"),
        ("сеул", "KR"), ("seoul", "KR"), ("корея", "KR"),
        ("вена", "AT"), ("vienna", "AT"),
        ("цюрих", "CH"), ("zurich", "CH"),
        ("стокгольм", "SE"), ("stockholm", "SE"),
        ("прага", "CZ"), ("prague", "CZ"),
        ("вильнюс", "LT"), ("рига", "LV"), ("таллин", "EE"),
        ("алматы", "KZ"), ("астана", "KZ"), ("ереван", "AM"), ("тбилиси", "GE"),
        ("мумбаи", "IN"), ("mumbai", "IN"),
        ("сингапур", "SG"), ("гонконг", "HK"),
        ("czech", "CZ"), ("turkiye", "TR"), ("türkiye", "TR"), ("korea", "KR"),
        ("emirates", "AE"),
    };

    /// <summary>Что пишут как код страны, хотя ISO такого кода не знает.</summary>
    private static readonly Dictionary<string, string> CodeAliases = new(StringComparer.Ordinal)
    {
        ["UK"] = "GB",
    };

    /// <summary>
    /// Двухбуквенные сокращения, которые в подписи ноды почти наверняка означают не страну.
    /// TV — телевидение (а не Тувалу), ID — идентификатор (а не Индонезия), HD и SD — качество.
    /// </summary>
    private static readonly HashSet<string> NotCountryTokens = new(StringComparer.Ordinal)
    {
        "TV", "ID", "HD", "SD", "PM", "AM",
    };

    private static readonly Lazy<HashSet<string>> KnownCodes = new(() =>
        CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Select(c => { try { return new RegionInfo(c.Name).TwoLetterISORegionName; } catch { return null; } })
            .Where(c => c is { Length: 2 } && c.All(char.IsAsciiLetterUpper))
            .Select(c => c!)
            .ToHashSet(StringComparer.Ordinal));

    /// <summary>
    /// Названия для поиска: русские из таблицы выше, английские и местные — от платформы.
    /// Отсортированы от длинного к короткому, чтобы «Nigeria» не превращалась в «Niger».
    /// </summary>
    private static readonly Lazy<(string Needle, string Code)[]> Needles = new(() =>
    {
        var list = new List<(string, string)>();

        foreach (var (code, ru) in RussianNames)
            list.Add((ru.ToLowerInvariant(), code));

        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            RegionInfo region;
            try { region = new RegionInfo(culture.Name); } catch { continue; }
            var code = region.TwoLetterISORegionName;
            if (code.Length != 2 || !code.All(char.IsAsciiLetterUpper)) continue;
            list.Add((region.EnglishName.ToLowerInvariant(), code));
            list.Add((region.NativeName.ToLowerInvariant(), code));
        }

        list.AddRange(Aliases.Select(a => (a.Needle, a.Code)));

        // короче трёх букв — не название, а случайное совпадение
        return list
            .Where(n => n.Item1.Length >= 3)
            .DistinctBy(n => n.Item1, StringComparer.Ordinal)
            .OrderByDescending(n => n.Item1.Length)
            .ToArray();
    });

    /// <summary>
    /// Группа «страна не определена». Нужна как обычный код, чтобы такие ноды можно было
    /// и увидеть в списке стран, и выключить — иначе они молча остаются в пуле навсегда.
    /// </summary>
    public const string Unknown = "??";

    /// <summary>ISO-код страны из подписи ноды, либо null если не распознано.</summary>
    public static string? ResolveCode(string remark)
    {
        if (string.IsNullOrWhiteSpace(remark)) return null;
        return FromFlagEmoji(remark) ?? FromName(remark) ?? FromCodeToken(remark);
    }

    /// <summary>
    /// Название страны на языке интерфейса. Русских названий у платформы нет,
    /// поэтому они из таблицы; для остального берётся английское название системы.
    /// </summary>
    public static string? DisplayName(string? code, string lang = "ru")
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 2) return null;
        var upper = code.ToUpperInvariant();

        if (Strings.Normalize(lang) == "ru")
        {
            var ru = RussianNames.FirstOrDefault(n => n.Code == upper).Ru;
            if (ru is not null) return ru;
        }

        try { return new RegionInfo(upper).EnglishName; }
        catch (ArgumentException) { return upper; }
    }

    /// <summary>Код страны → флаг: две буквы становятся региональными индикаторами Unicode.</summary>
    public static string Flag(string? code)
    {
        if (code is null || code.Length != 2) return "";
        var a = char.ToUpperInvariant(code[0]);
        var b = char.ToUpperInvariant(code[1]);
        if (a is < 'A' or > 'Z' || b is < 'A' or > 'Z') return "";
        return char.ConvertFromUtf32(0x1F1E6 + (a - 'A')) + char.ConvertFromUtf32(0x1F1E6 + (b - 'A'));
    }

    private static string? FromFlagEmoji(string text)
    {
        var runes = text.EnumerateRunes().ToArray();
        for (var i = 0; i + 1 < runes.Length; i++)
        {
            if (IsRegionalIndicator(runes[i].Value) && IsRegionalIndicator(runes[i + 1].Value))
            {
                var a = (char)('A' + (runes[i].Value - 0x1F1E6));
                var b = (char)('A' + (runes[i + 1].Value - 0x1F1E6));
                return new string(new[] { a, b });
            }
        }
        return null;
    }

    private static bool IsRegionalIndicator(int codepoint) => codepoint is >= 0x1F1E6 and <= 0x1F1FF;

    private static string? FromName(string remark)
    {
        var lower = remark.ToLowerInvariant();
        foreach (var (needle, code) in Needles.Value)
        {
            var at = lower.IndexOf(needle, StringComparison.Ordinal);
            while (at >= 0)
            {
                // только с начала слова: иначе «Bukarest» и «Fukuoka» ловятся на «uk»
                if (at == 0 || !char.IsLetterOrDigit(lower[at - 1])) return code;
                at = lower.IndexOf(needle, at + 1, StringComparison.Ordinal);
            }
        }
        return null;
    }

    /// <summary>
    /// Код страны отдельным словом: «DE-01», «[NL] node2», «US | Dallas».
    /// Только заглавные и только целым словом — иначе под правило попадает
    /// любая пара букв внутри имени сервера.
    /// </summary>
    private static string? FromCodeToken(string remark)
    {
        for (var i = 0; i + 1 < remark.Length; i++)
        {
            if (!char.IsAsciiLetterUpper(remark[i]) || !char.IsAsciiLetterUpper(remark[i + 1])) continue;
            if (i > 0 && char.IsLetterOrDigit(remark[i - 1])) continue;
            if (i + 2 < remark.Length && char.IsLetterOrDigit(remark[i + 2])) continue;

            var token = remark.Substring(i, 2);
            if (NotCountryTokens.Contains(token)) continue;
            // «100 GB», «500 MB» — это объём трафика, а не Великобритания с Молдавией
            if (PrecededByNumber(remark, i)) continue;

            if (CodeAliases.TryGetValue(token, out var mapped)) return mapped;
            if (KnownCodes.Value.Contains(token)) return token;
        }
        return null;
    }

    private static bool PrecededByNumber(string text, int at)
    {
        var i = at - 1;
        while (i >= 0 && text[i] == ' ') i--;
        return i >= 0 && char.IsDigit(text[i]);
    }
}
