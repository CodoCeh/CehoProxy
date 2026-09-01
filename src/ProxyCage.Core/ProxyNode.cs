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

public sealed class ProxyNode
{
    public required string Tag { get; set; }
    public ProxyProtocol Protocol { get; set; } = ProxyProtocol.Vless;
    public required string Server { get; set; }
    public int Port { get; set; }

    public string Credential { get; set; } = "";

    public string Network { get; set; } = "tcp";

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

    public string? Method { get; set; }

    public int AlterId { get; set; }

    public string? ObfsPassword { get; set; }

    public string? CongestionControl { get; set; }

    public string? TuicPassword { get; set; }

    public string Remark { get; set; } = "";
    public string? CountryCode { get; set; }
    public string? CountryName { get; set; }

    public string? Source { get; set; }

    public bool IsMeta { get; set; }

    public string Key => $"{Protocol}|{Server}|{Port}";
}
