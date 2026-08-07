namespace ProxyCage.Core;

public enum ProxyProtocol
{
    Vless,
    Vmess,
    Trojan,
    Shadowsocks,
    Hysteria2,
    Tuic,
}

/// <summary>Одна нода подписки. Заполняется <see cref="SubscriptionParser"/>.</summary>
public sealed class ProxyNode
{
    public required string Tag { get; set; }
    public ProxyProtocol Protocol { get; set; } = ProxyProtocol.Vless;
    public required string Server { get; set; }
    public int Port { get; set; }

    /// <summary>vless/vmess/tuic — uuid; trojan/hysteria2 — пароль; ss — пароль.</summary>
    public string Credential { get; set; } = "";

    /// <summary>tcp | grpc | ws | http | quic</summary>
    public string Network { get; set; } = "tcp";

    /// <summary>reality | tls | none</summary>
    public string Security { get; set; } = "none";

    public string? Flow { get; set; }
    public string? Sni { get; set; }
    public string? Fingerprint { get; set; }
    public string? PublicKey { get; set; }
    public string? ShortId { get; set; }
    public bool AllowInsecure { get; set; }
    public string? Alpn { get; set; }

    public string? ServiceName { get; set; }
    public string? Path { get; set; }
    public string? Host { get; set; }

    /// <summary>shadowsocks: aes-256-gcm, 2022-blake3-aes-128-gcm и т.п.</summary>
    public string? Method { get; set; }

    /// <summary>vmess: alterId.</summary>
    public int AlterId { get; set; }

    /// <summary>hysteria2: obfs-пароль (salamander).</summary>
    public string? ObfsPassword { get; set; }

    /// <summary>tuic: congestion control.</summary>
    public string? CongestionControl { get; set; }

    /// <summary>tuic: отдельный пароль при uuid в Credential.</summary>
    public string? TuicPassword { get; set; }

    public string Remark { get; set; } = "";
    public string? CountryCode { get; set; }
    public string? CountryName { get; set; }

    /// <summary>Имя подписки, из которой пришла нода. Пул общий, но происхождение видно.</summary>
    public string? Source { get; set; }

    /// <summary>Служебная запись подписки (Автовыбор, ключ роутера) — не нода выхода.</summary>
    public bool IsMeta { get; set; }

    /// <summary>
    /// Опознаватель ноды между перечитываниями подписки: тег меняется при каждой пересборке,
    /// а адрес и протокол — нет. По нему хранятся замеры задержки.
    /// </summary>
    public string Key => $"{Protocol}|{Server}|{Port}";
}
