using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProxyCage.Core;

public sealed class AppEntry
{
    public string Name { get; set; } = "";

    public string Folder { get; set; } = "";

    public string? Launch { get; set; }

    public bool VersionAgnostic { get; set; }

    public bool SingleFile { get; set; }

    public bool Enabled { get; set; } = true;
}

public sealed class SubscriptionEntry
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";

    public bool? LastCheckOk { get; set; }
    public string? LastCheckedUtc { get; set; }
}

public sealed class CehoConfig
{
    public int WebPort { get; set; } = 8899;

    public int MixedPort { get; set; } = 2080;

    public string TunAddress { get; set; } = "172.19.0.1/30";

    public List<AppEntry> Apps { get; set; } = new();
    public List<SubscriptionEntry> Subscriptions { get; set; } = new();

    public string? ActiveSubscription { get; set; }

    public List<string> PreferredCountries { get; set; } = new();

    public List<string> ExcludedCountries { get; set; } = new() { "RU" };

    public bool RotationEnabled { get; set; } = true;

    public int? MaxLatencyMs { get; set; }

    public Dictionary<string, int> NodeLatency { get; set; } = new();

    public string Language { get; set; } = "ru";

    public string? PasswordHash { get; set; }

    public string? PasswordSalt { get; set; }

    public bool SetupDone { get; set; }

    public string UpdateRepo { get; set; } = "CodoCeh/CehoProxy";

    public string CheckUrl { get; set; } = "https://www.gstatic.com/generate_204";

    public int TimeoutSeconds { get; set; } = 15;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static CehoConfig Load(string path) =>
        File.Exists(path)
            ? JsonSerializer.Deserialize<CehoConfig>(File.ReadAllText(path), Json) ?? new CehoConfig()
            : new CehoConfig();

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }
}
