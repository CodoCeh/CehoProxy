using System.Text.Json;

namespace ProxyCage.Core;

/// <summary>
/// Подписка приходит не только списком ссылок. Провайдеры и генераторы раздают ещё
/// готовые конфигурации целиком, и человек не обязан знать, что у него за формат:
/// он вставляет ссылку, а разбираться — наша работа.
///
/// Разобраны четыре формата помимо ссылок:
/// • xray/v2ray JSON — один конфиг или массив конфигов, у каждого свой «remarks»;
/// • sing-box JSON — плоский список outbounds;
/// • Clash / Clash.Meta YAML — раздел «proxies»;
/// • SIP008 — список серверов shadowsocks.
/// </summary>
public static class SubscriptionFormats
{
    /// <summary>Ноды из любого известного не-ссылочного формата; пусто — формат не наш.</summary>
    public static IReadOnlyList<ProxyNode> Parse(string text, string lang)
    {
        var trimmed = text.TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            return ParseJson(trimmed, lang);
        if (trimmed.Contains("proxies:", StringComparison.Ordinal))
            return ParseClash(trimmed, lang);
        return Array.Empty<ProxyNode>();
    }

    // ── JSON: xray, sing-box, SIP008 ──────────────────────────────────

    private static IReadOnlyList<ProxyNode> ParseJson(string text, string lang)
    {
        var nodes = new List<ProxyNode>();
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            // массив конфигов: так отдаёт генератор подписок — по конфигу на ноду
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray()) ReadContainer(item, nodes, lang);
                return nodes;
            }

            ReadContainer(root, nodes, lang);
        }
        catch (JsonException)
        {
            return Array.Empty<ProxyNode>();
        }
        return nodes;
    }

    private static void ReadContainer(JsonElement root, List<ProxyNode> nodes, string lang)
    {
        if (root.ValueKind != JsonValueKind.Object) return;

        // имя ноды у xray лежит на уровне всего конфига, а не outbound
        var remarks = Str(root, "remarks") ?? Str(root, "name") ?? "";

        if (root.TryGetProperty("outbounds", out var outbounds) && outbounds.ValueKind == JsonValueKind.Array)
        {
            foreach (var o in outbounds.EnumerateArray())
            {
                var node = ReadXrayOutbound(o, remarks, lang) ?? ReadSingBoxOutbound(o, remarks, lang);
                if (node is not null) nodes.Add(Number(node, nodes.Count + 1));
            }
        }

        // SIP008: список серверов shadowsocks
        if (root.TryGetProperty("servers", out var servers) && servers.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in servers.EnumerateArray())
            {
                if (Str(s, "server") is not { } host || Num(s, "server_port") is not { } port) continue;
                var name = Str(s, "remarks") ?? Str(s, "name") ?? "";
                nodes.Add(Number(Make(new ProxyNode
                {
                    Tag = "",
                    Protocol = ProxyProtocol.Shadowsocks,
                    Server = host,
                    Port = port,
                    Credential = Str(s, "password") ?? "",
                    Method = Str(s, "method"),
                    Network = "tcp",
                    Security = "none",
                }, name, lang), nodes.Count + 1));
            }
        }
    }

    /// <summary>xray: протокол в «protocol», адрес внутри settings, транспорт в streamSettings.</summary>
    private static ProxyNode? ReadXrayOutbound(JsonElement o, string remarks, string lang)
    {
        var protocol = Str(o, "protocol");
        if (protocol is null || !o.TryGetProperty("settings", out var settings)) return null;

        var kind = ProtocolOf(protocol);
        if (kind is null) return null;

        string server; int port; string credential; string? method = null, tuicPassword = null;
        var alterId = 0;

        if (settings.TryGetProperty("vnext", out var vnext) && vnext.ValueKind == JsonValueKind.Array
            && vnext.EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } first)
        {
            if (Str(first, "address") is not { } a || Num(first, "port") is not { } p) return null;
            server = a; port = p;
            var user = first.TryGetProperty("users", out var users) && users.ValueKind == JsonValueKind.Array
                ? users.EnumerateArray().FirstOrDefault()
                : default;
            credential = Str(user, "id") ?? "";
            alterId = Num(user, "alterId") ?? 0;
            method = Str(user, "security");
        }
        else if (settings.TryGetProperty("servers", out var srv) && srv.ValueKind == JsonValueKind.Array
                 && srv.EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } s)
        {
            if (Str(s, "address") is not { } a || Num(s, "port") is not { } p) return null;
            server = a; port = p;
            credential = Str(s, "password") ?? "";
            method = Str(s, "method");
        }
        else return null;

        var node = new ProxyNode
        {
            Tag = "",
            Protocol = kind.Value,
            Server = server,
            Port = port,
            Credential = credential,
            AlterId = alterId,
            Method = method,
            Network = "tcp",
            Security = "none",
            TuicPassword = tuicPassword,
        };

        if (o.TryGetProperty("streamSettings", out var stream)) ReadXrayStream(stream, node);

        var name = remarks.Length > 0 ? remarks : Str(o, "tag") ?? "";
        return Make(node, name, lang);
    }

    private static void ReadXrayStream(JsonElement stream, ProxyNode node)
    {
        node.Network = (Str(stream, "network") ?? "tcp").ToLowerInvariant();
        node.Security = (Str(stream, "security") ?? "none").ToLowerInvariant();

        if (stream.TryGetProperty("realitySettings", out var reality))
        {
            node.Sni = Str(reality, "serverName");
            node.PublicKey = Str(reality, "publicKey");
            node.ShortId = Str(reality, "shortId");
            node.Fingerprint = Str(reality, "fingerprint");
        }
        if (stream.TryGetProperty("tlsSettings", out var tls))
        {
            node.Sni ??= Str(tls, "serverName");
            node.Fingerprint ??= Str(tls, "fingerprint");
            node.AllowInsecure = Bool(tls, "allowInsecure");
        }
        if (stream.TryGetProperty("grpcSettings", out var grpc))
            node.ServiceName = Str(grpc, "serviceName");
        if (stream.TryGetProperty("wsSettings", out var ws))
        {
            node.Path = Str(ws, "path");
            if (ws.TryGetProperty("headers", out var h)) node.Host = Str(h, "Host") ?? Str(h, "host");
        }
        if (stream.TryGetProperty("httpSettings", out var http))
        {
            node.Path = Str(http, "path");
            node.Host = Str(http, "host");
        }
    }

    /// <summary>sing-box: плоско — «type», «server», «server_port», транспорт отдельным объектом.</summary>
    private static ProxyNode? ReadSingBoxOutbound(JsonElement o, string remarks, string lang)
    {
        var kind = ProtocolOf(Str(o, "type"));
        if (kind is null) return null;
        if (Str(o, "server") is not { } server || Num(o, "server_port") is not { } port) return null;

        var node = new ProxyNode
        {
            Tag = "",
            Protocol = kind.Value,
            Server = server,
            Port = port,
            Credential = Str(o, "uuid") ?? Str(o, "password") ?? "",
            Method = Str(o, "method"),
            Flow = Str(o, "flow"),
            Network = "tcp",
            Security = "none",
            ObfsPassword = o.TryGetProperty("obfs", out var obfs) ? Str(obfs, "password") : null,
        };

        if (kind is ProxyProtocol.Hysteria2 or ProxyProtocol.Tuic)
        {
            node.Network = "quic";
            node.Security = "tls";
            node.TuicPassword = Str(o, "password");
        }

        if (o.TryGetProperty("tls", out var tls) && Bool(tls, "enabled"))
        {
            node.Security = "tls";
            node.Sni = Str(tls, "server_name");
            node.AllowInsecure = Bool(tls, "insecure");
            if (tls.TryGetProperty("reality", out var reality) && Bool(reality, "enabled"))
            {
                node.Security = "reality";
                node.PublicKey = Str(reality, "public_key");
                node.ShortId = Str(reality, "short_id");
            }
            if (tls.TryGetProperty("utls", out var utls)) node.Fingerprint = Str(utls, "fingerprint");
        }

        if (o.TryGetProperty("transport", out var transport))
        {
            node.Network = (Str(transport, "type") ?? "tcp").ToLowerInvariant();
            node.ServiceName = Str(transport, "service_name");
            node.Path = Str(transport, "path");
            if (transport.TryGetProperty("headers", out var h)) node.Host = Str(h, "Host") ?? Str(h, "host");
        }

        var name = Str(o, "tag") ?? remarks;
        return Make(node, name, lang);
    }

    // ── Clash / Clash.Meta YAML ───────────────────────────────────────

    /// <summary>
    /// Читаем только раздел «proxies» и только те ключи, которые нужны ноде.
    /// Полноценный YAML тут не нужен: раздел всегда список плоских записей,
    /// а тащить ради него постороннюю библиотеку в самодостаточную сборку — лишнее.
    /// </summary>
    private static IReadOnlyList<ProxyNode> ParseClash(string text, string lang)
    {
        var nodes = new List<ProxyNode>();
        var lines = text.Replace("\r\n", "\n").Split('\n');

        var inProxies = false;
        Dictionary<string, string>? current = null;
        var currentIndent = -1;

        void Flush()
        {
            if (current is null) return;
            var node = FromClash(current, lang);
            if (node is not null) nodes.Add(Number(node, nodes.Count + 1));
            current = null;
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Length == 0 || line.TrimStart().StartsWith('#')) continue;

            var indent = line.Length - line.TrimStart().Length;
            var body = line.TrimStart();

            if (!inProxies)
            {
                if (body.StartsWith("proxies:", StringComparison.Ordinal)) { inProxies = true; currentIndent = -1; }
                continue;
            }

            // следующий раздел верхнего уровня — раздел с нодами кончился
            if (indent == 0 && !body.StartsWith('-')) { Flush(); break; }

            if (body.StartsWith("- ", StringComparison.Ordinal) || body == "-")
            {
                Flush();
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                currentIndent = indent;
                var rest = body.Length > 1 ? body[1..].Trim() : "";
                if (rest.StartsWith('{')) { ReadInline(rest, current); Flush(); }
                else if (rest.Length > 0) ReadPair(rest, current);
                continue;
            }

            if (current is not null && indent > currentIndent) ReadPair(body, current);
        }
        Flush();
        return nodes;
    }

    /// <summary>Однострочная запись: - {name: NL-1, type: vless, server: a.com, port: 443, ...}</summary>
    private static void ReadInline(string text, Dictionary<string, string> into)
    {
        var inner = text.Trim().TrimStart('{').TrimEnd('}');
        foreach (var part in SplitTop(inner)) ReadPair(part, into);
    }

    /// <summary>Делим по запятым верхнего уровня: внутри могут быть вложенные скобки и кавычки.</summary>
    private static IEnumerable<string> SplitTop(string text)
    {
        var depth = 0;
        var quote = '\0';
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
            if (c is '"' or '\'') { quote = c; continue; }
            if (c is '{' or '[') depth++;
            else if (c is '}' or ']') depth--;
            else if (c == ',' && depth == 0) { yield return text[start..i]; start = i + 1; }
        }
        if (start < text.Length) yield return text[start..];
    }

    private static void ReadPair(string text, Dictionary<string, string> into)
    {
        var colon = text.IndexOf(':');
        if (colon <= 0) return;
        var key = text[..colon].Trim();
        var value = text[(colon + 1)..].Trim().Trim('"', '\'');
        if (key.Length > 0) into[key] = value;
    }

    private static ProxyNode? FromClash(Dictionary<string, string> f, string lang)
    {
        string? V(string k) => f.TryGetValue(k, out var v) && v.Length > 0 ? v : null;

        var kind = ProtocolOf(V("type"));
        if (kind is null) return null;
        if (V("server") is not { } server || !int.TryParse(V("port"), out var port)) return null;

        var tls = V("tls") is "true" or "1";
        var network = (V("network") ?? "tcp").ToLowerInvariant();

        var node = new ProxyNode
        {
            Tag = "",
            Protocol = kind.Value,
            Server = server,
            Port = port,
            Credential = V("uuid") ?? V("password") ?? "",
            Method = V("cipher"),
            Flow = V("flow"),
            AlterId = int.TryParse(V("alterId"), out var aid) ? aid : 0,
            Network = kind is ProxyProtocol.Hysteria2 or ProxyProtocol.Tuic ? "quic" : network,
            Security = kind is ProxyProtocol.Hysteria2 or ProxyProtocol.Tuic or ProxyProtocol.Trojan || tls
                ? "tls" : "none",
            Sni = V("sni") ?? V("servername"),
            Fingerprint = V("client-fingerprint"),
            AllowInsecure = V("skip-cert-verify") is "true" or "1",
            ServiceName = V("grpc-service-name"),
            Path = V("ws-path") ?? V("path"),
            ObfsPassword = V("obfs-password"),
            TuicPassword = V("password"),
        };

        if (V("public-key") is { } pbk) { node.Security = "reality"; node.PublicKey = pbk; }
        if (V("short-id") is { } sid) node.ShortId = sid;

        return Make(node, V("name") ?? "", lang);
    }

    // ── общее ─────────────────────────────────────────────────────────

    private static ProxyProtocol? ProtocolOf(string? name) => name?.ToLowerInvariant() switch
    {
        "vless" => ProxyProtocol.Vless,
        "vmess" => ProxyProtocol.Vmess,
        "trojan" => ProxyProtocol.Trojan,
        "shadowsocks" or "ss" => ProxyProtocol.Shadowsocks,
        "hysteria2" or "hy2" => ProxyProtocol.Hysteria2,
        "tuic" => ProxyProtocol.Tuic,
        _ => null,
    };

    private static ProxyNode Make(ProxyNode node, string remark, string lang)
    {
        var country = CountryResolver.ResolveCode(remark);
        node.Remark = remark;
        node.CountryCode = country;
        node.CountryName = CountryResolver.DisplayName(country, lang);
        node.IsMeta = SubscriptionParser.IsServiceEntry(remark);
        return node;
    }

    private static ProxyNode Number(ProxyNode node, int index)
    {
        node.Tag = $"n{index:00}";
        return node;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            }
            : null;

    private static int? Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.Number => v.TryGetInt32(out var n) ? n : null,
                JsonValueKind.String => int.TryParse(v.GetString(), out var s) ? s : null,
                _ => null,
            }
            : null;

    private static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
        && (v.ValueKind == JsonValueKind.True
            || (v.ValueKind == JsonValueKind.String && v.GetString() is "true" or "1"));
}
