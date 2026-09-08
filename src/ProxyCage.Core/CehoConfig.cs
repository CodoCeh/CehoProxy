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

    /// <summary>
    /// Ноды, через которые ходит эта программа. Пусто — общие правила пула.
    /// Ключи те же, что у BlockedNodes: «Vless|server|443».
    /// </summary>
    public List<string> AllowedNodes { get; set; } = new();
}

public sealed class SubscriptionEntry
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";

    /// <summary>Выключенная подписка остаётся в списке со своими данными, но нод в пул не даёт.</summary>
    public bool Enabled { get; set; } = true;

    public bool? LastCheckOk { get; set; }
    public string? LastCheckedUtc { get; set; }

    public DateTime? ExpiresUtc { get; set; }
    public long? UsedBytes { get; set; }
    public long? TotalBytes { get; set; }

    public int? LastNodes { get; set; }
    public string? LastError { get; set; }
}

public sealed class CehoConfig
{
    public int WebPort { get; set; } = 8899;

    public int MixedPort { get; set; } = 2080;

    /// <summary>
    /// Свой адрес туннеля. Не 172.19.0.1: это заводской адрес sing-box, его же ставит Happ
    /// и другие клиенты. Если совпасть, уборка следов принимает чужой адаптер за свой.
    /// </summary>
    public const string DefaultTunAddress = "172.31.211.1/30";

    /// <summary>Заводской адрес движка: его нельзя считать нашим только по совпадению.</summary>
    public const string SharedSingBoxTun = "172.19.0.1/30";

    public string TunAddress { get; set; } = DefaultTunAddress;

    public List<AppEntry> Apps { get; set; } = new();
    public List<SubscriptionEntry> Subscriptions { get; set; } = new();

    public string? ActiveSubscription { get; set; }

    public List<string> PreferredCountries { get; set; } = new();

    public List<string> ExcludedCountries { get; set; } = new() { "RU" };

    /// <summary>
    /// Ноды, выключенные вручную: страна разрешена, а именно эта нода в пул не идёт.
    /// Хранятся ключами вида «Vless|server|443», чтобы выбор жил после обновления подписки.
    /// </summary>
    public List<string> BlockedNodes { get; set; } = new();

    public bool RotationEnabled { get; set; } = true;

    public int? MaxLatencyMs { get; set; }

    public Dictionary<string, int> NodeLatency { get; set; } = new();

    public string Language { get; set; } = "ru";

    public string? PasswordHash { get; set; }

    public string? PasswordSalt { get; set; }

    public bool SetupDone { get; set; }

    public string UpdateRepo { get; set; } = "CodoCeh/CehoProxy";

    public string CheckUrl { get; set; } = "https://www.gstatic.com/generate_204";

    public int TimeoutSeconds { get; set; } = 45;

    /// <summary>Подробность лога движка: debug помогает разобрать падение, warn — обычная работа.</summary>
    public string EngineLogLevel { get; set; } = "warn";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static CehoConfig Load(string path)
    {
        var cfg = File.Exists(path)
            ? JsonSerializer.Deserialize<CehoConfig>(File.ReadAllText(path), Json) ?? new CehoConfig()
            : new CehoConfig();

        foreach (var app in cfg.Apps)
            app.AllowedNodes ??= new();

        // Старые установки жили на заводском адресе движка — том же, что у Happ.
        if (SharesSingBoxTun(cfg.TunAddress))
        {
            cfg.TunAddress = DefaultTunAddress;
            if (File.Exists(path))
            {
                try { cfg.Save(path); }
                catch { /* адрес всё равно уже в памяти, движок получит его при сборке правил */ }
            }
        }

        return cfg;
    }

    public static bool SharesSingBoxTun(string? address) =>
        string.Equals(address?.Trim(), SharedSingBoxTun, StringComparison.OrdinalIgnoreCase);

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }
}
