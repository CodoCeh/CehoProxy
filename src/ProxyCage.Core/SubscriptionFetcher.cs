using System.Net.Http.Headers;

namespace ProxyCage.Core;

/// <summary>
/// Заголовки и разбор ответов при скачивании подписок.
/// Панели Remnawave/3x-ui с HWID-limit отдают 404 без тела, если клиент не похож на sing-box/Clash.
/// </summary>
public enum SubscriptionFetchPersona
{
    SingBox,
    Exclave,
    Clash,
    Client,
    Browser,
}

public static class SubscriptionFetcher
{
    public const int MaxAttempts = 5;

    /// <summary>sing-box for Android (SFA) — часто ожидают панели Remnawave/HWID-limit.</summary>
    private const string SingBoxUserAgent = "SFA/1.12.0 (612; sing-box 1.12.0; language en)";

    /// <summary>Stock Exclave (Android) — см. ExclaveNetwork/Exclave RawUpdater.kt.</summary>
    private const string ExclaveUserAgent = "Exclave/0.17.56";

    public static SubscriptionFetchPersona PersonaOf(int attempt) => attempt switch
    {
        1 => SubscriptionFetchPersona.SingBox,
        2 => SubscriptionFetchPersona.Exclave,
        3 => SubscriptionFetchPersona.Clash,
        4 => SubscriptionFetchPersona.Client,
        5 => SubscriptionFetchPersona.Browser,
        _ => SubscriptionFetchPersona.SingBox,
    };

    public static void ApplyPersonaHeaders(
        HttpRequestHeaders headers, SubscriptionFetchPersona persona, string productVersion, string hwid)
    {
        switch (persona)
        {
            case SubscriptionFetchPersona.Browser:
                headers.TryAddWithoutValidation("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/127.0.0.0 Safari/537.36");
                headers.TryAddWithoutValidation("Accept",
                    "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
                headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                break;
            case SubscriptionFetchPersona.Exclave:
                headers.TryAddWithoutValidation("User-Agent", ExclaveUserAgent);
                headers.TryAddWithoutValidation("Accept", "*/*");
                break;
            case SubscriptionFetchPersona.Clash:
                headers.TryAddWithoutValidation("User-Agent", "clash-meta/1.18.0");
                headers.TryAddWithoutValidation("Accept", "*/*");
                headers.TryAddWithoutValidation("profile-update-interval", "24");
                break;
            case SubscriptionFetchPersona.SingBox:
                headers.TryAddWithoutValidation("User-Agent", SingBoxUserAgent);
                headers.TryAddWithoutValidation("Accept", "*/*");
                break;
            default:
                headers.TryAddWithoutValidation("User-Agent", "CehoProxy/" + productVersion);
                headers.TryAddWithoutValidation("Accept", "*/*");
                break;
        }

        headers.TryAddWithoutValidation("x-hwid", hwid);
        headers.TryAddWithoutValidation("x-device-os",
            Os.IsWindows ? "Windows" : Os.IsMac ? "macOS" : "Linux");
        headers.TryAddWithoutValidation("x-ver-os", Environment.OSVersion.Version.ToString());
        headers.TryAddWithoutValidation("x-device-model", DeviceModelName());
    }

    internal static string DeviceModelName()
    {
        try
        {
            var name = Environment.MachineName.Trim();
            if (name.Length >= 2) return name.Length <= 64 ? name : name[..64];
        }
        catch { }

        return Os.IsWindows ? "Windows PC" : Os.IsMac ? "Mac" : "Linux PC";
    }

    public static bool HeaderTrue(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
        && values.Any(v => v.Equals("true", StringComparison.OrdinalIgnoreCase));

    /// <summary>HWID-ошибка панели — по заголовкам или телу ответа.</summary>
    public static string? HwidFailureMessage(HttpResponseMessage response, string? body, string lang)
    {
        if (HeaderTrue(response, "x-hwid-max-devices-reached") || HeaderTrue(response, "x-hwid-limit"))
            return Strings.T(lang, "sub_hwid_limit");

        if (HeaderTrue(response, "x-hwid-not-supported")
            || (HeaderTrue(response, "x-hwid-active") && response.StatusCode == System.Net.HttpStatusCode.NotFound))
            return Strings.T(lang, "sub_hwid_gate");

        if (!string.IsNullOrEmpty(body) && SubscriptionParser.LooksLikeHwidGate(body))
            return Strings.T(lang, "sub_hwid_gate");

        return null;
    }

    public static string HttpFailureMessage(HttpResponseMessage response, string lang)
    {
        var code = (int)response.StatusCode;
        return Strings.T(lang, code >= 500 ? "diag_server_down" : "diag_http_error", code);
    }
}
