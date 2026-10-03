namespace ProxyCage.Core;

/// <summary>Cheap startup blockers, evaluated again for every manual or automatic start.</summary>
public static class StartupPreflight
{
    public static string? ConfigurationError(CehoConfig config, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        string T(string ru, string en) => config.Language == "ru" ? ru : en;
        var ports = new[] { config.WebPort, config.MixedPort, config.ClashApiPort };
        if (ports.Any(p => p is < 1 or > 65535) || ports.Distinct().Count() != ports.Length)
            return T("Порты панели, прокси и API должны быть разными числами от 1 до 65535. Исправьте настройки сети.",
                "Panel, proxy and API ports must be different numbers between 1 and 65535. Correct the network settings.");
        if (!config.Apps.Any(a => a.Enabled && !string.IsNullOrWhiteSpace(a.Folder)))
            return Strings.T(config.Language, "pf_no_apps") + " " + Strings.T(config.Language, "pf_no_apps_fix");
        if (!config.Subscriptions.Any(s => s.Enabled))
            return Strings.T(config.Language, config.Subscriptions.Count == 0 ? "pf_no_subs" : "subs_all_off");
        if (!config.Subscriptions.Any(s => s.Enabled && IsUsableSource(s.Url, fileExists)))
            return T("Нет включённой подписки с корректной ссылкой или локальным файлом. Проверьте подписки.",
                "No enabled subscription has a valid link or local file. Check subscriptions.");
        return null;
    }

    public static bool IsUsableSource(string? source, Func<string, bool>? fileExists = null)
    {
        if (string.IsNullOrWhiteSpace(source)) return false;
        source = source.Trim();
        if (SubscriptionParser.LooksLikeNodeUri(source)) return true;
        if ((fileExists ?? File.Exists)(source)) return true;
        return Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrWhiteSpace(uri.Host);
    }
}
