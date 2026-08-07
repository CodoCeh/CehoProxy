using System.Text;
using System.Text.Json;
using System.Web;

namespace ProxyCage.Core;

/// <summary>
/// Разбирает подписку (base64-блоб или голый список ссылок) в список <see cref="ProxyNode"/>.
/// Поддержаны vless, vmess, trojan, shadowsocks, hysteria2, tuic.
/// </summary>
public static class SubscriptionParser
{
    /// <summary>
    /// Служебные записи подписки — не ноды выхода, а «автовыбор» провайдера: та же нода
    /// под другим именем. Раньше сюда же попадала любая запись с нераспознанной страной,
    /// и подписка с непривычными подписями теряла ВСЕ ноды разом. Проверено живьём.
    /// </summary>
    private static readonly string[] MetaMarkers =
        { "автовыбор", "авто выбор", "автоматический выбор", "auto", "url-test", "urltest",
          "балансировщик", "balancer" };

    private static readonly (string Scheme, ProxyProtocol Protocol)[] Schemes =
    {
        ("vless://", ProxyProtocol.Vless),
        ("vmess://", ProxyProtocol.Vmess),
        ("trojan://", ProxyProtocol.Trojan),
        ("ss://", ProxyProtocol.Shadowsocks),
        ("hysteria2://", ProxyProtocol.Hysteria2),
        ("hy2://", ProxyProtocol.Hysteria2),
        ("tuic://", ProxyProtocol.Tuic),
    };

    /// <summary>
    /// Это уже сама нода, а не адрес, по которому её надо скачивать.
    ///
    /// Человеку часто выдают не подписку, а одну ссылку на ноду — и он вставляет её туда же,
    /// куда вставляют подписку. Отказывать за это не за что: разберём на месте.
    /// </summary>
    public static bool LooksLikeNodeUri(string text) =>
        Schemes.Any(x => text.TrimStart().StartsWith(x.Scheme, StringComparison.OrdinalIgnoreCase));

    /// <summary>Язык нужен только для названия страны: код один, а показываем его человеку.</summary>
    public static IReadOnlyList<ProxyNode> Parse(string subscriptionBody, string lang = "ru")
    {
        var text = Decode(subscriptionBody);

        // подписка может быть не списком ссылок, а готовой конфигурацией: xray, sing-box,
        // Clash, SIP008. Человек вставляет то, что ему выдали, и знать формат не обязан
        var ready = SubscriptionFormats.Parse(text, lang);
        if (ready.Count > 0) return ready;

        var nodes = new List<ProxyNode>();
        var index = 0;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            var match = Schemes.FirstOrDefault(s =>
                line.StartsWith(s.Scheme, StringComparison.OrdinalIgnoreCase));
            if (match.Scheme is null) continue;

            var node = match.Protocol switch
            {
                ProxyProtocol.Vmess => ParseVmess(line, ++index, lang),
                ProxyProtocol.Shadowsocks => ParseShadowsocks(line, ++index, lang),
                _ => ParseUriStyle(line, match.Scheme, match.Protocol, ++index, lang),
            };
            if (node is not null) nodes.Add(node);
        }

        return nodes;
    }

    private static string Decode(string body)
    {
        if (Schemes.Any(s => body.Contains(s.Scheme, StringComparison.OrdinalIgnoreCase)))
            return body;

        try
        {
            var cleaned = body.Trim().Replace("\n", "").Replace("\r", "")
                              .Replace('-', '+').Replace('_', '/');
            switch (cleaned.Length % 4)
            {
                case 2: cleaned += "=="; break;
                case 3: cleaned += "="; break;
            }
            return Encoding.UTF8.GetString(Convert.FromBase64String(cleaned));
        }
        catch (FormatException)
        {
            return body;
        }
    }

    /// <summary>vless / trojan / hysteria2 / tuic: scheme://credential@host:port?query#remark</summary>
    private static ProxyNode? ParseUriStyle(string uri, string scheme, ProxyProtocol protocol, int index, string lang)
    {
        try
        {
            var rest = uri[scheme.Length..];

            var fragment = "";
            var hashIdx = rest.IndexOf('#');
            if (hashIdx >= 0)
            {
                fragment = HttpUtility.UrlDecode(rest[(hashIdx + 1)..]);
                rest = rest[..hashIdx];
            }

            var query = "";
            var qIdx = rest.IndexOf('?');
            if (qIdx >= 0)
            {
                query = rest[(qIdx + 1)..];
                rest = rest[..qIdx];
            }

            var atIdx = rest.LastIndexOf('@');
            if (atIdx < 0) return null;

            var credential = HttpUtility.UrlDecode(rest[..atIdx]);

            // tuic несёт логин как uuid:password — без разделения sing-box ругается на uuid
            string? tuicPassword = null;
            if (protocol == ProxyProtocol.Tuic)
            {
                var sep = credential.IndexOf(':');
                if (sep > 0)
                {
                    tuicPassword = credential[(sep + 1)..];
                    credential = credential[..sep];
                }
            }
            var hostPort = rest[(atIdx + 1)..];
            var colonIdx = hostPort.LastIndexOf(':');
            if (colonIdx < 0 || !int.TryParse(hostPort[(colonIdx + 1)..], out var port)) return null;

            var q = HttpUtility.ParseQueryString(query);
            var country = CountryResolver.ResolveCode(fragment);

            var node = new ProxyNode
            {
                Tag = $"n{index:00}",
                Protocol = protocol,
                Server = hostPort[..colonIdx],
                Port = port,
                Credential = credential,
                Network = (q["type"] ?? (protocol is ProxyProtocol.Hysteria2 or ProxyProtocol.Tuic ? "quic" : "tcp")).ToLowerInvariant(),
                Security = (q["security"] ?? (protocol is ProxyProtocol.Trojan or ProxyProtocol.Hysteria2 or ProxyProtocol.Tuic ? "tls" : "none")).ToLowerInvariant(),
                Flow = Empty(q["flow"]),
                Sni = q["sni"] ?? q["peer"],
                Fingerprint = q["fp"],
                PublicKey = q["pbk"],
                ShortId = q["sid"],
                ServiceName = q["serviceName"],
                Path = q["path"],
                Host = q["host"],
                Alpn = q["alpn"],
                AllowInsecure = q["allowInsecure"] == "1" || q["insecure"] == "1",
                ObfsPassword = q["obfs-password"],
                CongestionControl = q["congestion_control"],
                TuicPassword = tuicPassword ?? q["password"],
                Remark = fragment,
                CountryCode = country,
                CountryName = CountryResolver.DisplayName(country, lang),
            };
            node.IsMeta = IsMeta(fragment, country);
            return node;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>vmess://&lt;base64 JSON&gt;</summary>
    private static ProxyNode? ParseVmess(string uri, int index, string lang)
    {
        try
        {
            var payload = uri["vmess://".Length..].Trim();
            var hashIdx = payload.IndexOf('#');
            if (hashIdx >= 0) payload = payload[..hashIdx];

            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            var root = doc.RootElement;

            string? S(string name) =>
                root.TryGetProperty(name, out var v)
                    ? v.ValueKind == JsonValueKind.Number ? v.GetRawText() : v.GetString()
                    : null;

            if (S("add") is not { } server || S("port") is not { } portText
                || !int.TryParse(portText, out var port)) return null;

            var remark = S("ps") ?? "";
            var country = CountryResolver.ResolveCode(remark);
            var tls = S("tls");

            var node = new ProxyNode
            {
                Tag = $"n{index:00}",
                Protocol = ProxyProtocol.Vmess,
                Server = server,
                Port = port,
                Credential = S("id") ?? "",
                AlterId = int.TryParse(S("aid"), out var aid) ? aid : 0,
                Network = (S("net") ?? "tcp").ToLowerInvariant(),
                Security = string.IsNullOrEmpty(tls) || tls == "none" ? "none" : "tls",
                Sni = S("sni") ?? Empty(S("host")),
                Host = Empty(S("host")),
                Path = Empty(S("path")),
                ServiceName = Empty(S("path")),
                Method = S("scy"),
                Remark = remark,
                CountryCode = country,
                CountryName = CountryResolver.DisplayName(country, lang),
            };
            node.IsMeta = IsMeta(remark, country);
            return node;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>ss://base64(method:password)@host:port#remark, либо целиком base64.</summary>
    private static ProxyNode? ParseShadowsocks(string uri, int index, string lang)
    {
        try
        {
            var rest = uri["ss://".Length..];

            var remark = "";
            var hashIdx = rest.IndexOf('#');
            if (hashIdx >= 0)
            {
                remark = HttpUtility.UrlDecode(rest[(hashIdx + 1)..]);
                rest = rest[..hashIdx];
            }

            var qIdx = rest.IndexOf('?');
            var query = qIdx >= 0 ? rest[(qIdx + 1)..] : "";
            if (qIdx >= 0) rest = rest[..qIdx];

            string method, password, hostPort;
            var atIdx = rest.LastIndexOf('@');
            if (atIdx >= 0)
            {
                var userInfo = rest[..atIdx];
                hostPort = rest[(atIdx + 1)..];
                var decoded = TryBase64(userInfo) ?? HttpUtility.UrlDecode(userInfo);
                var sep = decoded.IndexOf(':');
                if (sep < 0) return null;
                method = decoded[..sep];
                password = decoded[(sep + 1)..];
            }
            else
            {
                var decoded = TryBase64(rest);
                if (decoded is null) return null;
                var at = decoded.LastIndexOf('@');
                if (at < 0) return null;
                var creds = decoded[..at];
                hostPort = decoded[(at + 1)..];
                var sep = creds.IndexOf(':');
                if (sep < 0) return null;
                method = creds[..sep];
                password = creds[(sep + 1)..];
            }

            var colonIdx = hostPort.LastIndexOf(':');
            if (colonIdx < 0 || !int.TryParse(hostPort[(colonIdx + 1)..], out var port)) return null;

            var q = HttpUtility.ParseQueryString(query);
            var country = CountryResolver.ResolveCode(remark);

            var node = new ProxyNode
            {
                Tag = $"n{index:00}",
                Protocol = ProxyProtocol.Shadowsocks,
                Server = hostPort[..colonIdx],
                Port = port,
                Credential = password,
                Method = method,
                Network = "tcp",
                Security = "none",
                Path = q["path"],
                Host = q["host"],
                Remark = remark,
                CountryCode = country,
                CountryName = CountryResolver.DisplayName(country, lang),
            };
            node.IsMeta = IsMeta(remark, country);
            return node;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryBase64(string s)
    {
        try
        {
            var cleaned = s.Replace('-', '+').Replace('_', '/');
            switch (cleaned.Length % 4)
            {
                case 2: cleaned += "=="; break;
                case 3: cleaned += "="; break;
            }
            var bytes = Convert.FromBase64String(cleaned);
            var text = Encoding.UTF8.GetString(bytes);
            return text.Contains(':') ? text : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? Empty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    /// <summary>
    /// Служебная запись узнаётся по маркеру, и только целым словом: без этого «auto»
    /// внутри имени сервера выкидывало бы обычную ноду. Нераспознанная страна служебной
    /// записью НЕ считается — такая нода работает не хуже прочих.
    /// </summary>
    internal static bool IsServiceEntry(string remark) => IsMeta(remark, null);

    private static bool IsMeta(string remark, string? country)
    {
        var lower = remark.ToLowerInvariant();
        foreach (var marker in MetaMarkers)
        {
            var at = lower.IndexOf(marker, StringComparison.Ordinal);
            while (at >= 0)
            {
                var before = at == 0 || !char.IsLetterOrDigit(lower[at - 1]);
                var afterAt = at + marker.Length;
                var after = afterAt >= lower.Length || !char.IsLetterOrDigit(lower[afterAt]);
                if (before && after) return true;
                at = lower.IndexOf(marker, at + 1, StringComparison.Ordinal);
            }
        }
        return false;
    }
}
