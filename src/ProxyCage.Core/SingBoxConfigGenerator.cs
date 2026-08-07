using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProxyCage.Core;

/// <summary>
/// «В пуле не осталось нод» — отдельная беда, а не любая ошибка сборки правил.
///
/// По ней откатываются настройки, которыми человек только что сузил пул: страны, порог
/// скорости. Без своего типа откатывалось бы и то, что к пулу отношения не имеет —
/// например «не выбрано ни одной программы», и человек терял бы верную настройку.
/// </summary>
public sealed class PoolEmptyException : InvalidOperationException
{
    public PoolEmptyException(string message) : base(message) { }
}

public static class SingBoxConfigGenerator
{
    private const string ProxyTag = "proxy";
    private const string DirectTag = "direct";

    public static string Generate(IReadOnlyList<ProxyNode> allNodes, ProxyCageSettings settings)
    {
        var pool = allNodes
            .Where(n => !n.IsMeta)
            .Where(n => !settings.ExcludedExitCountries.Contains(n.CountryCode ?? CountryResolver.Unknown))
            .ToList();

        if (pool.Count == 0)
            throw new InvalidOperationException(
                "После фильтра по странам выхода не осталось ни одной ноды. Ослабь исключения стран.");

        if (string.IsNullOrWhiteSpace(settings.FolderPath))
            throw new InvalidOperationException("Не задана папка приложения (FolderPath).");

        var folderRegex = FolderPathToRegex(settings.FolderPath);

        var outbounds = new JsonArray();
        var poolTags = new JsonArray();
        foreach (var node in pool)
        {
            outbounds.Add(OutboundBuilder.Build(node));
            poolTags.Add(node.Tag);
        }

        outbounds.Add(new JsonObject
        {
            ["type"] = "urltest",
            ["tag"] = ProxyTag,
            ["outbounds"] = poolTags,
            ["url"] = settings.UrlTestUrl,
            ["interval"] = "3m",
            ["tolerance"] = 50,
        });
        outbounds.Add(new JsonObject { ["type"] = "direct", ["tag"] = DirectTag });

        var config = new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = settings.LogLevel, ["timestamp"] = true },
            ["experimental"] = new JsonObject
            {
                ["clash_api"] = new JsonObject
                {
                    ["external_controller"] = $"127.0.0.1:{settings.ClashApiPort}",
                },
            },
            ["dns"] = BuildDns(folderRegex, settings.TunAddress),
            ["inbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "tun",
                    ["tag"] = "tun-in",
                    ["address"] = new JsonArray { settings.TunAddress },
                    ["auto_route"] = true,
                    ["strict_route"] = true,
                    ["stack"] = "gvisor",
                },
                new JsonObject
                {
                    ["type"] = "mixed",
                    ["tag"] = "mixed-in",
                    ["listen"] = "127.0.0.1",
                    ["listen_port"] = settings.MixedPort,
                },
            },
            ["outbounds"] = outbounds,
            ["route"] = BuildRoute(folderRegex, settings),
        };

        return config.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        });
    }

    /// <summary>Локальный SOCKS/HTTP-прокси без TUN.</summary>
    public static string GenerateLocalProxy(IReadOnlyList<ProxyNode> allNodes, ProxyCageSettings settings)
    {
        var pool = allNodes
            .Where(n => !n.IsMeta)
            .Where(n => !settings.ExcludedExitCountries.Contains(n.CountryCode ?? CountryResolver.Unknown))
            .ToList();

        if (pool.Count == 0)
            throw new InvalidOperationException(
                "После фильтра по странам выхода не осталось ни одной ноды. Ослабь исключения стран.");

        var outbounds = new JsonArray();
        var poolTags = new JsonArray();
        foreach (var node in pool)
        {
            outbounds.Add(OutboundBuilder.Build(node));
            poolTags.Add(node.Tag);
        }

        outbounds.Add(new JsonObject
        {
            ["type"] = "urltest",
            ["tag"] = ProxyTag,
            ["outbounds"] = poolTags,
            ["url"] = settings.UrlTestUrl,
            ["interval"] = "3m",
            ["tolerance"] = 50,
        });
        outbounds.Add(new JsonObject { ["type"] = "direct", ["tag"] = DirectTag });

        var config = new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = settings.LogLevel, ["timestamp"] = true },
            ["dns"] = new JsonObject
            {
                ["servers"] = DnsServersWithDirect(settings.TunAddress),
                ["rules"] = new JsonArray
                {
                    new JsonObject { ["inbound"] = new JsonArray { "mixed-in" }, ["server"] = "dns-proxy" },
                },
                ["final"] = "dns-direct",
                ["strategy"] = "prefer_ipv4",
            },
            ["inbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "mixed",
                    ["tag"] = "mixed-in",
                    ["listen"] = "127.0.0.1",
                    ["listen_port"] = settings.MixedPort,
                },
            },
            ["outbounds"] = outbounds,
            ["route"] = new JsonObject
            {
                ["rules"] = new JsonArray
                {
                    new JsonObject { ["action"] = "sniff" },
                    new JsonObject { ["inbound"] = new JsonArray { "mixed-in" }, ["outbound"] = ProxyTag },
                },
                ["final"] = ProxyTag,
                ["auto_detect_interface"] = true,
                ["default_domain_resolver"] = new JsonObject { ["server"] = "dns-direct" },
            },
        };

        return config.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        });
    }

    /// <summary>
    /// Боевой конфиг CehoProxy: TUN + правила по КАЖДОМУ включённому приложению из конфига.
    /// Fail-closed: трафик приложений идёт только в пул нод, на direct не откатывается —
    /// если ни одна нода не жива, соединение рвётся, а не утекает мимо туннеля.
    /// </summary>
    public static string GenerateForConfig(IReadOnlyList<ProxyNode> allNodes, CehoConfig cfg)
    {
        var apps = cfg.Apps.Where(a => a.Enabled && !string.IsNullOrWhiteSpace(a.Folder)).ToList();
        if (apps.Count == 0)
            throw new InvalidOperationException("Не добавлено ни одного приложения — изолировать нечего.");

        var pool = FilterByCountries(allNodes, cfg);

        var appRegexes = new JsonArray();
        foreach (var a in apps) appRegexes.Add(AppDetector.ToRegex(a));

        var outbounds = new JsonArray();
        var poolTags = new JsonArray();
        foreach (var node in pool)
        {
            outbounds.Add(OutboundBuilder.Build(node));
            poolTags.Add(node.Tag);
        }
        outbounds.Add(new JsonObject
        {
            ["type"] = "urltest",
            ["tag"] = ProxyTag,
            ["outbounds"] = poolTags,
            ["url"] = "https://www.gstatic.com/generate_204",
            ["interval"] = "3m",
            ["tolerance"] = 50,
        });
        outbounds.Add(new JsonObject { ["type"] = "direct", ["tag"] = DirectTag });

        var config = new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = "warn", ["timestamp"] = true },
            ["dns"] = new JsonObject
            {
                ["servers"] = DnsServersWithDirect(cfg.TunAddress),
                // DNS изолированных приложений — через туннель, иначе резолвинг течёт мимо
                ["rules"] = new JsonArray
                {
                    new JsonObject { ["process_path_regex"] = appRegexes.DeepClone(), ["server"] = "dns-proxy" },
                },
                ["final"] = "dns-direct",
                ["strategy"] = "prefer_ipv4",
            },
            ["inbounds"] = new JsonArray
            {
                BuildTun(cfg),
                new JsonObject
                {
                    ["type"] = "mixed",
                    ["tag"] = "mixed-in",
                    ["listen"] = "127.0.0.1",
                    ["listen_port"] = cfg.MixedPort,
                },
            },
            ["outbounds"] = outbounds,
            ["route"] = new JsonObject
            {
                ["rules"] = new JsonArray
                {
                    new JsonObject { ["action"] = "sniff" },
                    // без этого DNS всей системы уходит в никуда (адрес внутри TUN-подсети)
                    new JsonObject { ["protocol"] = "dns", ["action"] = "hijack-dns" },
                    new JsonObject { ["inbound"] = new JsonArray { "mixed-in" }, ["outbound"] = ProxyTag },
                    new JsonObject { ["process_path_regex"] = appRegexes.DeepClone(), ["outbound"] = ProxyTag },
                },
                ["final"] = DirectTag,          // всё прочее — мимо туннеля, система не затронута
                ["auto_detect_interface"] = true,
                ["default_domain_resolver"] = new JsonObject { ["server"] = "dns-direct" },
            },
        };

        return config.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        });
    }


    /// <summary>
    /// Пул нод после фильтра по странам. Пустой пул — это не «упало», а понятное сообщение
    /// с перечислением снятых стран: пользователь сам их снял и должен видеть, что именно вернуть.
    /// </summary>
    public static List<ProxyNode> FilterByCountries(IReadOnlyList<ProxyNode> allNodes, CehoConfig cfg)
    {
        var pool = allNodes
            .Where(n => !n.IsMeta)
            // нода без распознанной страны попадает в группу «??»: её видно в списке стран
            // и её можно выключить, как любую другую. Молча выбрасывать такие ноды нельзя —
            // так подписка с непривычными подписями теряла весь пул
            .Where(n => !cfg.ExcludedCountries.Contains(n.CountryCode ?? CountryResolver.Unknown,
                                                        StringComparer.OrdinalIgnoreCase))
            .Where(n => cfg.PreferredCountries.Count == 0
                        || cfg.PreferredCountries.Contains(n.CountryCode ?? CountryResolver.Unknown,
                                                           StringComparer.OrdinalIgnoreCase))
            .Where(n => !IsTooSlow(n, cfg))
            .ToList();

        if (pool.Count == 0 && cfg.MaxLatencyMs is { } limit
            && allNodes.Any(n => !n.IsMeta && IsTooSlow(n, cfg)))
            throw new PoolEmptyException(Strings.T(cfg.Language, "speed_none_left", limit));

        if (pool.Count == 0)
        {
            var reason = cfg.PreferredCountries.Count > 0
                ? string.Join(", ", cfg.PreferredCountries)
                : string.Join(", ", cfg.ExcludedCountries);
            throw new PoolEmptyException(Strings.T(cfg.Language, "countries_none_left", reason));
        }

        return pool;
    }

    /// <summary>
    /// Нода отсеивается по скорости, только если её ДЕЙСТВИТЕЛЬНО мерили и она не уложилась.
    /// Без замера нода остаётся в пуле: «не измеряли» и «медленная» — разные вещи.
    /// </summary>
    public static bool IsTooSlow(ProxyNode node, CehoConfig cfg) =>
        cfg.MaxLatencyMs is { } limit
        && cfg.NodeLatency.TryGetValue(node.Key, out var ms)
        && ms > limit;

    /// <summary>
    /// TUN-вход. Расхождения между системами тут не косметические:
    /// strict_route существует только на Linux и Windows; на Linux мы дополнительно
    /// закрепляем имя интерфейса и СВОИ индексы таблицы и правил iproute2 — иначе после
    /// аварийного завершения непонятно, чей мусор снимать, и можно снести чужой туннель.
    /// </summary>
    private static JsonObject BuildTun(CehoConfig cfg)
    {
        var tun = new JsonObject
        {
            ["type"] = "tun",
            ["tag"] = "tun-in",
            ["address"] = new JsonArray { cfg.TunAddress },
            ["auto_route"] = true,
            ["stack"] = "gvisor",
        };

        if (!Os.IsMac) tun["strict_route"] = true;

        if (Os.IsLinux)
        {
            tun["interface_name"] = TunCleanup.LinuxInterfaceName;
            tun["iproute2_table_index"] = TunCleanup.Iproute2TableIndex;
            tun["iproute2_rule_index"] = TunCleanup.Iproute2RuleIndex;
        }

        return tun;
    }

    private static JsonObject BuildDns(string folderRegex, string tunAddress)
    {
        // DNS приложений из папки — через прокси (нет утечки резолвинга), остальное — локально.
        var servers = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "https",
                ["tag"] = "dns-proxy",
                ["server"] = "1.1.1.1",
                ["detour"] = ProxyTag,
            },
        };
        foreach (var direct in DirectDnsServers(tunAddress)) servers.Add(direct!.DeepClone());

        return new JsonObject
        {
            ["servers"] = servers,
            ["rules"] = new JsonArray
            {
                new JsonObject
                {
                    ["process_path_regex"] = new JsonArray { folderRegex },
                    ["server"] = "dns-proxy",
                },
            },
            ["final"] = "dns-direct",
            ["strategy"] = "prefer_ipv4",
        };
    }

    /// <summary>Резолвер для туннеля плюс настоящие DNS машины для всего остального.</summary>
    private static JsonArray DnsServersWithDirect(string tunAddress)
    {
        var servers = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "https", ["tag"] = "dns-proxy",
                ["server"] = "1.1.1.1", ["detour"] = ProxyTag,
            },
        };
        foreach (var direct in DirectDnsServers(tunAddress)) servers.Add(direct!.DeepClone());
        return servers;
    }

    /// <summary>
    /// Куда спрашивать имена для ВСЕГО, что мимо туннеля.
    ///
    /// Нельзя писать «спрашивай систему» (type: local): с поднятым TUN система спрашивает
    /// наш же туннель, и получается петля — машина перестаёт резолвить что угодно.
    /// Поймано живьём на Windows. Поэтому берём настоящие адреса DNS машины и ходим
    /// в них НАПРЯМУЮ, мимо туннеля. Не нашлись — публичный резолвер, тоже напрямую:
    /// пусть лучше имена резолвятся не тем сервером, чем не резолвятся вовсе.
    /// </summary>
    private static JsonArray DirectDnsServers(string tunAddress)
    {
        var servers = new JsonArray();
        var system = Os.SystemDnsServers(tunAddress);

        if (system.Count == 0)
        {
            servers.Add(new JsonObject
            {
                ["type"] = "udp", ["tag"] = "dns-direct",
                ["server"] = "1.1.1.1", ["detour"] = DirectTag,
            });
            return servers;
        }

        for (var i = 0; i < system.Count; i++)
            servers.Add(new JsonObject
            {
                ["type"] = "udp",
                ["tag"] = i == 0 ? "dns-direct" : $"dns-direct-{i + 1}",
                ["server"] = system[i],
                ["detour"] = DirectTag,
            });
        return servers;
    }

    private static JsonObject BuildRoute(string folderRegex, ProxyCageSettings settings)
    {
        var ruleSet = new JsonArray();
        var blockedTags = new JsonArray();
        foreach (var cc in settings.BlockedDestinationCountries)
        {
            var lower = cc.ToLowerInvariant();
            var tag = $"geoip-{lower}";
            ruleSet.Add(new JsonObject
            {
                ["type"] = "local",
                ["tag"] = tag,
                ["format"] = "binary",
                ["path"] = Path.Combine(settings.RuleSetDir, $"geoip-{lower}.srs").Replace('\\', '/'),
            });
            blockedTags.Add(tag);
        }

        var rules = new JsonArray
        {
            new JsonObject { ["action"] = "sniff" },
            // системные DNS-запросы, захваченные TUN (auto_route переключает DNS
            // ОС на адрес внутри самой TUN-подсети) — обязаны уйти в свой DNS-модуль
            // (rules/final из секции dns), а не трактоваться как обычное соединение:
            // без этого правила sing-box пытается "direct"-ом достучаться до адреса
            // внутри собственной /30-подсети TUN, это никогда не сработает и рвёт
            // резолвинг для ВСЕЙ системы, а не только для папки.
            new JsonObject { ["protocol"] = "dns", ["action"] = "hijack-dns" },
            // локальный вход (проба страны выхода) — всегда через прокси
            new JsonObject
            {
                ["inbound"] = new JsonArray { "mixed-in" },
                ["outbound"] = ProxyTag,
            },
        };

        // приложения папки → запрещённое назначение → reject
        if (blockedTags.Count > 0)
        {
            rules.Add(new JsonObject
            {
                ["process_path_regex"] = new JsonArray { folderRegex },
                ["rule_set"] = blockedTags,
                ["action"] = "reject",
            });
        }

        // приложения папки → прокси (kill-switch: сюда попадает весь их трафик,
        // на direct они не откатываются)
        rules.Add(new JsonObject
        {
            ["process_path_regex"] = new JsonArray { folderRegex },
            ["outbound"] = ProxyTag,
        });

        var route = new JsonObject();
        if (ruleSet.Count > 0)
            route["rule_set"] = ruleSet;
        route["rules"] = rules;
        route["final"] = DirectTag; // всё прочее — напрямую, не затрагивается
        route["auto_detect_interface"] = true;
        // адреса нод (домены) резолвим напрямую, иначе замкнутый круг с прокси
        route["default_domain_resolver"] = new JsonObject { ["server"] = "dns-direct" };
        return route;
    }

    /// <summary>
    /// Папка → Go-regex, матчащий любой процесс под ней (вкл. дочерние), регистронезависимо.
    /// C:\Games\App → (?i)^C:\\Games\\App[\\/]
    /// </summary>
    internal static string FolderPathToRegex(string folderPath)
    {
        var trimmed = folderPath.TrimEnd('\\', '/');
        var sb = new StringBuilder("(?i)^");
        foreach (var ch in trimmed)
        {
            if ("\\.+*?()|[]{}^$".Contains(ch))
                sb.Append('\\');
            sb.Append(ch);
        }
        sb.Append("[\\\\/]");
        return sb.ToString();
    }
}
