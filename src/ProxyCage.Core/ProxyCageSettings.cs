namespace ProxyCage.Core;

/// <summary>Параметры генерации конфига sing-box. Все страны — ISO alpha-2.</summary>
public sealed class ProxyCageSettings
{
    /// <summary>Папка приложения: все процессы под ней пойдут через прокси.</summary>
    public string FolderPath { get; set; } = "";

    /// <summary>Страны ВЫХОДА, чьи ноды не берём в пул (регулируется чек-листом в UI).</summary>
    public HashSet<string> ExcludedExitCountries { get; set; } =
        new(StringComparer.OrdinalIgnoreCase) { "RU", "NL" };

    /// <summary>Страны НАЗНАЧЕНИЯ, трафик к которым режется (нужны geoip-*.srs в <see cref="RuleSetDir"/>).</summary>
    public List<string> BlockedDestinationCountries { get; set; } = new() { "RU", "NL" };

    /// <summary>Папка с файлами geoip-&lt;cc&gt;.srs.</summary>
    public string RuleSetDir { get; set; } = "rulesets";

    public int ClashApiPort { get; set; } = 9090;

    /// <summary>Локальный SOCKS/HTTP-вход (проба страны выхода + ручное использование).</summary>
    public int MixedPort { get; set; } = 2080;

    public string TunAddress { get; set; } = "172.19.0.1/30";
    public string UrlTestUrl { get; set; } = "https://www.gstatic.com/generate_204";
    public string LogLevel { get; set; } = "warn";
}
