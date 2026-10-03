using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProxyCage.Core;

public sealed class AppEntry
{
    public string Name { get; set; } = "";

    /// <summary>Подпись в списке; пусто — автоимя из exe (<see cref="Name"/>).</summary>
    public string? DisplayName { get; set; }

    [JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName.Trim();

    public string Folder { get; set; } = "";

    /// <summary>Canonical selected application path, independent of the routing folder.</summary>
    public string? IdentityPath { get; set; }

    /// <summary>Путь к exe для запуска; пусто — папка.</summary>
    public string? Launch { get; set; }

    public bool VersionAgnostic { get; set; }

    public bool SingleFile { get; set; }

    public bool Enabled { get; set; } = true;

    public bool NoInternet { get; set; }

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

    /// <summary>Clash API sing-box для urltest-задержек в панели.</summary>
    public int ClashApiPort { get; set; } = 9090;

    /// <summary>
    /// Свой адрес туннеля. Не 172.19.0.1: это заводской адрес sing-box, его же ставит Happ
    /// и другие клиенты. Если совпасть, уборка следов принимает чужой адаптер за свой.
    /// </summary>
    public const string DefaultTunAddress = "172.31.211.1/30";

    /// <summary>Заводской адрес движка: его нельзя считать нашим только по совпадению.</summary>
    public const string SharedSingBoxTun = "172.19.0.1/30";

    public string TunAddress { get; set; } = DefaultTunAddress;

    public const string TunAddress6 = "fdce:b5:211::1/126";

    public const string GuardTunAddress = "172.31.211.5/30";

    public const string GuardTunAddress6 = "fdce:b5:211::5/126";

    public bool TunIpv6 { get; set; } = true;

    public bool FailClosed { get; set; } = true;

    public const string PanelModeSimple = "simple";

    public const string PanelModePro = "pro";

    public string? PanelMode { get; set; }

    [JsonIgnore]
    public bool SimplePanel => string.Equals(PanelMode, PanelModeSimple, StringComparison.OrdinalIgnoreCase);

    public List<AppEntry> Apps { get; set; } = new();
    public List<SubscriptionEntry> Subscriptions { get; set; } = new();

    /// <summary>NaiveProxy / Caddy upstream (sing-box naive outbound).</summary>
    public NaiveProxySettings NaiveProxy { get; set; } = new();

    public string? ActiveSubscription { get; set; }

    public List<string> PreferredCountries { get; set; } = new();

    public List<string> ExcludedCountries { get; set; } = new() { "RU" };

    /// <summary>Адреса из вкладки «Сайты», без пути. Куда они идут, решает <see cref="SiteMode"/>.</summary>
    public List<string> DirectSites { get; set; } = new();

    /// <summary>Адрес сайта → код страны нод. Пусто — сайт идёт по режиму списка, без своей страны.</summary>
    public Dictionary<string, string> SiteCountries { get; set; } = new();

    public const string SiteModeExcept = "except";

    public const string SiteModeOnly = "only";

    /// <summary>except — всё через туннель, кроме списка. only — через туннель только список.</summary>
    public string SiteMode { get; set; } = SiteModeExcept;

    [JsonIgnore]
    public bool SitesOnly => string.Equals(SiteMode, SiteModeOnly, StringComparison.OrdinalIgnoreCase);

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

    public bool AutostartOffered { get; set; }

    public string UpdateRepo { get; set; } = "CodoCeh/CehoProxy";

    public string CheckUrl { get; set; } = "https://www.gstatic.com/generate_204";

    public int TimeoutSeconds { get; set; } = 45;

    /// <summary>Подробность лога движка: debug помогает разобрать падение, warn — обычная работа.</summary>
    public string EngineLogLevel { get; set; } = "warn";

    public bool AutoUpdate { get; set; }

    public bool TrayControls { get; set; }

    public bool EngineAutoUpdate { get; set; } = true;

    public string? AutoUpdatedVersion { get; set; }

    public DateTime? AutoUpdatedAtUtc { get; set; }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Порт для «chp open». config.json часто читает только root, а браузер
    /// открывает человек за компьютером: если секрет недоступен, берём panel.port.
    /// </summary>
    public static int ReadWebPort(string configPath, string root)
    {
        try
        {
            if (File.Exists(configPath))
                return Load(configPath).WebPort;
        }
        catch { }

        return Auth.ReadPanelPointer(root) ?? new CehoConfig().WebPort;
    }

    public static CehoConfig Load(string path)
    {
        PrivateFile.RejectLink(path);
        var cfg = File.Exists(path)
            ? JsonSerializer.Deserialize<CehoConfig>(PrivateFile.ReadText(path, MaxFileBytes), Json)
                ?? throw new InvalidDataException("Configuration must be a JSON object, not null.")
            : new CehoConfig { PanelMode = PanelModeSimple };
        cfg.PanelMode = string.Equals(cfg.PanelMode, PanelModeSimple, StringComparison.OrdinalIgnoreCase)
            ? PanelModeSimple
            : PanelModePro;

        if (cfg.Apps is not null)
            foreach (var app in cfg.Apps)
                if (app is not null) app.AllowedNodes ??= new();
        cfg.DirectSites ??= new();
        cfg.SiteCountries ??= new();
        if (!cfg.SitesOnly)
            cfg.SiteMode = SiteModeExcept;

        // Reject corruption before migrations can write back to the original file.
        cfg.Validate();

        cfg.MigrateLegacyNaive(path);

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

    internal const int MaxFileBytes = 32 * 1024 * 1024;

    /// <summary>Validate local settings before replacing a file or starting an apply.</summary>
    public void Validate()
    {
        if (WebPort is < 1 or > 65535 || MixedPort is < 1 or > 65535 || ClashApiPort is < 1 or > 65535)
            throw new InvalidDataException("Configuration ports must be between 1 and 65535.");
        if (TimeoutSeconds <= 0 || MaxLatencyMs < 0)
            throw new InvalidDataException("Configuration timeouts and latency limits are invalid.");
        var address = TunAddress?.Split('/');
        if (address is not { Length: 2 } || !System.Net.IPAddress.TryParse(address[0], out var ip)
            || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || !int.TryParse(address[1], out var prefix) || prefix is < 0 or > 32)
            throw new InvalidDataException("The tunnel address must be an IPv4 address with a valid prefix.");
        if (!Uri.TryCreate(CheckUrl, UriKind.Absolute, out var check) || check.Scheme is not ("http" or "https"))
            throw new InvalidDataException("The connection check URL must use HTTP or HTTPS.");
        if (!string.IsNullOrWhiteSpace(EngineLogLevel) && EngineLogLevel is not ("trace" or "debug" or "info" or "warn" or "error" or "fatal" or "panic"))
            throw new InvalidDataException("The engine log level is invalid.");
        if (Apps is null || Subscriptions is null || NaiveProxy is null || SiteCountries is null || NodeLatency is null
            || new[] { PreferredCountries, ExcludedCountries, DirectSites, BlockedNodes }.Any(l => l is null || l.Any(s => s is null))
            || SiteCountries.Any(p => p.Value is null))
            throw new InvalidDataException("Configuration lists cannot be null or contain null entries.");
        if (Apps.Any(a => a is null || a.Name is null || a.Folder is null || a.AllowedNodes is null || a.AllowedNodes.Any(n => n is null)))
            throw new InvalidDataException("An application entry is invalid.");
        if (Subscriptions.Any(s => s is null || !IsSafeSubscriptionName(s.Name) || s.Url is null)
            || Subscriptions.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Subscriptions.Count)
            throw new InvalidDataException("Subscription names must be unique, nonempty local file names.");
        if (NaiveProxy.Server is null || NaiveProxy.Username is null || NaiveProxy.Password is null || NaiveProxy.Remark is null)
            throw new InvalidDataException("Legacy proxy settings are invalid.");
    }

    internal static bool IsSafeSubscriptionName(string? name) => !string.IsNullOrWhiteSpace(name)
        && name.Length <= 192 && name.IndexOfAny(new[] { '/', '\\', ':', '<', '>', '"', '|', '?', '*' }) < 0
        && !name.Any(char.IsControl);

    public void Save(string path) => Save(path, null);

    internal void Save(string path, Action<PrivateFile.WriteStage, string>? observe)
    {
        Validate();
        var text = JsonSerializer.Serialize(this, Json);
        if (System.Text.Encoding.UTF8.GetByteCount(text) > MaxFileBytes)
            throw new InvalidDataException("Configuration file is too large.");
        PrivateFile.Write(path, text, observe);
    }

    /// <summary>Сохраняет сведения о проверке подписки, не помечая правила движка устаревшими.</summary>
    public void SaveSubscriptionStatus(string path)
    {
        var previousWriteTime = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : (DateTime?)null;
        Save(path);
        if (previousWriteTime is { } value)
            File.SetLastWriteTimeUtc(path, value);
    }

    /// <summary>Старый блок NaiveProxy в config.json переносим в подписку naive://…</summary>
    internal void MigrateLegacyNaive(string path)
    {
        if (!NaiveProxy.IsConfigured) return;

        var uri = NaiveProxyHelper.BuildUri(NaiveProxy);
        if (!Subscriptions.Any(s => string.Equals(s.Url, uri, StringComparison.OrdinalIgnoreCase)))
        {
            var baseName = string.IsNullOrWhiteSpace(NaiveProxy.Remark) ? "NaiveProxy" : NaiveProxy.Remark.Trim();
            if (!IsSafeSubscriptionName(baseName)) baseName = "NaiveProxy";
            var name = baseName;
            for (var i = 2; Subscriptions.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)); i++)
                name = $"{baseName[..Math.Min(baseName.Length, 180)]} {i}";

            Subscriptions.Add(new SubscriptionEntry { Name = name, Url = uri, Enabled = true });
            ActiveSubscription ??= name;
        }

        NaiveProxy = new NaiveProxySettings();
        if (File.Exists(path))
        {
            try { Save(path); }
            catch { /* в памяти уже без legacy-блока */ }
        }
    }
}
