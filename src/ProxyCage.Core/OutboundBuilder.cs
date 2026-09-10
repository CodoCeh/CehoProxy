using System.Text.Json.Nodes;

namespace ProxyCage.Core;

public static class OutboundBuilder
{
    public static JsonObject Build(ProxyNode n) => n.Protocol switch
    {
        ProxyProtocol.Vless => BuildVless(n),
        ProxyProtocol.Vmess => BuildVmess(n),
        ProxyProtocol.Trojan => BuildTrojan(n),
        ProxyProtocol.Shadowsocks => BuildShadowsocks(n),
        ProxyProtocol.Hysteria2 => BuildHysteria2(n),
        ProxyProtocol.Tuic => BuildTuic(n),
        ProxyProtocol.Naive => BuildNaive(n),
        _ => throw new NotSupportedException($"протокол {n.Protocol} не поддержан"),
    };

    private static JsonObject Base(ProxyNode n, string type) => new()
    {
        ["type"] = type,
        ["tag"] = n.Tag,
        ["server"] = n.Server,
        ["server_port"] = n.Port,
    };

    private static JsonObject BuildVless(ProxyNode n)
    {
        var o = Base(n, "vless");
        o["uuid"] = n.Credential;
        if (!string.IsNullOrEmpty(n.Flow) && n.Network == "tcp") o["flow"] = n.Flow;
        AddTls(o, n);
        AddTransport(o, n);
        return o;
    }

    private static JsonObject BuildVmess(ProxyNode n)
    {
        var o = Base(n, "vmess");
        o["uuid"] = n.Credential;
        o["alter_id"] = n.AlterId;
        o["security"] = string.IsNullOrEmpty(n.Method) ? "auto" : n.Method;
        AddTls(o, n);
        AddTransport(o, n);
        return o;
    }

    private static JsonObject BuildTrojan(ProxyNode n)
    {
        var o = Base(n, "trojan");
        o["password"] = n.Credential;
        AddTls(o, n, forceTls: true);
        AddTransport(o, n);
        return o;
    }

    private static JsonObject BuildShadowsocks(ProxyNode n)
    {
        var o = Base(n, "shadowsocks");
        o["method"] = string.IsNullOrEmpty(n.Method) ? "aes-256-gcm" : n.Method;
        o["password"] = n.Credential;
        return o;
    }

    private static JsonObject BuildHysteria2(ProxyNode n)
    {
        var o = Base(n, "hysteria2");
        o["password"] = n.Credential;
        if (!string.IsNullOrEmpty(n.ObfsPassword))
            o["obfs"] = new JsonObject { ["type"] = "salamander", ["password"] = n.ObfsPassword };
        AddTls(o, n, forceTls: true);
        return o;
    }

    private static JsonObject BuildTuic(ProxyNode n)
    {
        var o = Base(n, "tuic");
        o["uuid"] = n.Credential;
        o["password"] = n.TuicPassword ?? "";
        o["congestion_control"] = string.IsNullOrEmpty(n.CongestionControl) ? "bbr" : n.CongestionControl;
        AddTls(o, n, forceTls: true);
        return o;
    }

    private static JsonObject BuildNaive(ProxyNode n)
    {
        var o = Base(n, "naive");
        o["username"] = n.Credential;
        o["password"] = n.TuicPassword ?? "";
        var tls = new JsonObject
        {
            ["enabled"] = true,
            ["server_name"] = n.Sni ?? n.Server,
        };
        if (n.AllowInsecure) tls["insecure"] = true;
        o["tls"] = tls;
        // Dial Fields: у naive domain_strategy резолвит только hostname ноды (не UDP в туннеле).
        // С 1.12 — domain_resolver; domain_strategy оставляем для движков до 1.12.
        o["domain_resolver"] = new JsonObject
        {
            ["server"] = "dns-direct",
            ["strategy"] = "ipv4_only",
        };
        o["domain_strategy"] = "ipv4_only";
        o["quic"] = false;
        return o;
    }

    private static void AddTls(JsonObject o, ProxyNode n, bool forceTls = false)
    {
        if (!forceTls && n.Security is not ("tls" or "reality")) return;

        var tls = new JsonObject
        {
            ["enabled"] = true,
            ["server_name"] = n.Sni ?? n.Server,
        };
        if (n.AllowInsecure) tls["insecure"] = true;
        if (!string.IsNullOrEmpty(n.Alpn))
            tls["alpn"] = new JsonArray(n.Alpn.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                              .Select(a => (JsonNode)a.Trim()).ToArray());

        if (n.Protocol is not (ProxyProtocol.Hysteria2 or ProxyProtocol.Tuic))
            tls["utls"] = new JsonObject
            {
                ["enabled"] = true,
                ["fingerprint"] = string.IsNullOrEmpty(n.Fingerprint) ? "chrome" : n.Fingerprint,
            };

        if (n.Security == "reality")
            tls["reality"] = new JsonObject
            {
                ["enabled"] = true,
                ["public_key"] = n.PublicKey ?? "",
                ["short_id"] = n.ShortId ?? "",
            };

        o["tls"] = tls;
    }

    /// <summary>
    /// Cursor и другие HTTP/2-клиенты держат длинные стримы; без keepalive gRPC-канал
    /// к ноде засыпает и рвёт их каждые ~20 с — в UI это «Reconnecting».
    /// </summary>
    private static JsonObject GrpcTransport(string? serviceName) => new()
    {
        ["type"] = "grpc",
        ["service_name"] = serviceName ?? "",
        ["idle_timeout"] = "60s",
        ["ping_timeout"] = "20s",
        ["permit_without_stream"] = true,
    };

    private static void AddTransport(JsonObject o, ProxyNode n)
    {
        switch (n.Network)
        {
            case "grpc":
                o["transport"] = GrpcTransport(n.ServiceName);
                break;
            case "ws":
                var ws = new JsonObject
                {
                    ["type"] = "ws",
                    ["path"] = n.Path ?? "/",
                    ["idle_timeout"] = "60s",
                    ["ping_timeout"] = "20s",
                };
                if (!string.IsNullOrEmpty(n.Host))
                    ws["headers"] = new JsonObject { ["Host"] = n.Host };
                o["transport"] = ws;
                break;
            case "http":
                o["transport"] = new JsonObject
                {
                    ["type"] = "http",
                    ["path"] = n.Path ?? "/",
                    ["idle_timeout"] = "60s",
                    ["ping_timeout"] = "20s",
                };
                break;
        }
    }
}
