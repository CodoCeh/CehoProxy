namespace ProxyCage.Core;

public sealed class ProxyCageSettings
{
    public string FolderPath { get; set; } = "";

    public HashSet<string> ExcludedExitCountries { get; set; } =
        new(StringComparer.OrdinalIgnoreCase) { "RU", "NL" };

    public List<string> BlockedDestinationCountries { get; set; } = new() { "RU", "NL" };

    public string RuleSetDir { get; set; } = "rulesets";

    public int ClashApiPort { get; set; } = 9090;

    public int MixedPort { get; set; } = 2080;

    public string TunAddress { get; set; } = "172.19.0.1/30";
    public string UrlTestUrl { get; set; } = "https://www.gstatic.com/generate_204";
    public string LogLevel { get; set; } = "warn";
}
