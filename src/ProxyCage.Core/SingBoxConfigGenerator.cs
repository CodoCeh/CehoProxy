using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProxyCage.Core;

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
    /// Движок пишет в свой вывод, а мы его читаем и складываем в общий журнал:
    /// своего файла у движка нет, время ставит журнал.
    /// </summary>
    private static JsonObject BuildLog(CehoConfig cfg) => new()
    {
        ["level"] = string.IsNullOrWhiteSpace(cfg.EngineLogLevel) ? "warn" : cfg.EngineLogLevel,
        ["timestamp"] = false,
    };

    public static string AppOutboundTag(int appIndex) => $"proxy-app-{appIndex}";

    public static string AppDnsTag(int appIndex) => $"dns-proxy-app-{appIndex}";

    public static bool HasNodeFilter(AppEntry app) =>
        app.AllowedNodes is { Count: > 0 };

    public static List<ProxyNode> ResolvePinned(AppEntry app, IReadOnlyList<ProxyNode> allNodes)
    {
        if (!HasNodeFilter(app)) return new List<ProxyNode>();
        var keys = app.AllowedNodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return allNodes.Where(n => !n.IsMeta && keys.Contains(n.Key)).ToList();
    }

    public static string GenerateForConfig(IReadOnlyList<ProxyNode> allNodes, CehoConfig cfg)
    {
        var apps = cfg.Apps.Where(a => a.Enabled && !string.IsNullOrWhiteSpace(a.Folder)).ToList();
        if (apps.Count == 0)
            throw new InvalidOperationException("Не добавлено ни одного приложения — изолировать нечего.");

        var pool = BuildPool(allNodes, cfg);
        var pinned = apps.Select((a, i) => (App: a, Index: i, Nodes: ResolvePinned(a, allNodes)))
            .Where(x => HasNodeFilter(x.App))
            .ToList();
        var unpinned = apps.Where(a => !HasNodeFilter(a)).ToList();

        var engineNodes = new List<ProxyNode>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in pool.Concat(pinned.SelectMany(x => x.Nodes)))
        {
            if (seen.Add(node.Key)) engineNodes.Add(node);
        }

        var checkUrl = string.IsNullOrWhiteSpace(cfg.CheckUrl)
            ? "https://www.gstatic.com/generate_204"
            : cfg.CheckUrl;

        var outbounds = new JsonArray();
        foreach (var node in engineNodes)
            outbounds.Add(OutboundBuilder.Build(node));

        var poolTags = new JsonArray();
        foreach (var node in pool) poolTags.Add(node.Tag);
        outbounds.Add(UrlTest(ProxyTag, poolTags, checkUrl));

        foreach (var item in pinned.Where(x => x.Nodes.Count > 0))
        {
            var tags = new JsonArray();
            foreach (var node in item.Nodes) tags.Add(node.Tag);
            outbounds.Add(UrlTest(AppOutboundTag(item.Index), tags, checkUrl));
        }

        outbounds.Add(new JsonObject { ["type"] = "direct", ["tag"] = DirectTag });

        var dnsServers = DnsServersWithDirect(cfg.TunAddress);
        var dnsRules = new JsonArray();
        var hijack = new JsonArray();
        var routeRules = new JsonArray { new JsonObject { ["action"] = "sniff" } };

        foreach (var item in pinned)
        {
            var regex = new JsonArray { AppDetector.ToRegex(item.App) };
            if (item.Nodes.Count == 0)
            {
                routeRules.Add(new JsonObject
                {
                    ["protocol"] = "dns",
                    ["process_path_regex"] = regex.DeepClone(),
                    ["action"] = "reject",
                });
                routeRules.Add(new JsonObject
                {
                    ["process_path_regex"] = regex.DeepClone(),
                    ["action"] = "reject",
                });
                continue;
            }

            var dnsTag = AppDnsTag(item.Index);
            dnsServers.Insert(1, new JsonObject
            {
                ["type"] = "https",
                ["tag"] = dnsTag,
                ["server"] = "1.1.1.1",
                ["detour"] = AppOutboundTag(item.Index),
            });
            dnsRules.Add(new JsonObject
            {
                ["process_path_regex"] = regex.DeepClone(),
                ["server"] = dnsTag,
            });
            hijack.Add(regex.DeepClone());
            routeRules.Add(new JsonObject
            {
                ["process_path_regex"] = regex.DeepClone(),
                ["outbound"] = AppOutboundTag(item.Index),
            });
        }

        if (unpinned.Count > 0)
        {
            var regexes = new JsonArray();
            foreach (var a in unpinned) regexes.Add(AppDetector.ToRegex(a));
            dnsRules.Add(new JsonObject { ["process_path_regex"] = regexes.DeepClone(), ["server"] = "dns-proxy" });
            hijack.Add(regexes.DeepClone());
            routeRules.Add(new JsonObject { ["process_path_regex"] = regexes.DeepClone(), ["outbound"] = ProxyTag });
        }

        // Перехватываем только запросы имён от выбранных программ. Запросы остальной
        // системы проходят насквозь к её обычному серверу имён: чужие имена не наше дело.
        var hijackRegexes = FlattenRegexes(hijack);
        if (hijackRegexes.Count > 0)
        {
            routeRules.Insert(1, new JsonObject
            {
                ["protocol"] = "dns",
                ["process_path_regex"] = hijackRegexes,
                ["action"] = "hijack-dns",
            });
        }

        // Windows видит наш адаптер и заодно спрашивает имена у него. Молчать в ответ
        // нельзя — система будет ждать и тормозить, поэтому отвечаем через её же сервер.
        var tunHijackIndex = hijackRegexes.Count > 0 ? 2 : 1;
        routeRules.Insert(tunHijackIndex, new JsonObject
        {
            ["protocol"] = "dns",
            ["ip_cidr"] = new JsonArray { cfg.TunAddress },
            ["action"] = "hijack-dns",
        });
        routeRules.Insert(tunHijackIndex + 1, new JsonObject
        {
            ["inbound"] = new JsonArray { "mixed-in" },
            ["outbound"] = ProxyTag,
        });

        var config = new JsonObject
        {
            ["log"] = BuildLog(cfg),
            ["dns"] = new JsonObject
            {
                ["servers"] = dnsServers,
                ["rules"] = dnsRules,
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
                ["find_process"] = true,
                ["rules"] = routeRules,
                ["final"] = DirectTag,
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

    private static JsonObject UrlTest(string tag, JsonArray outboundTags, string url) => new()
    {
        ["type"] = "urltest",
        ["tag"] = tag,
        ["outbounds"] = outboundTags,
        ["url"] = url,
        ["interval"] = "3m",
        ["tolerance"] = 50,
    };

    private static JsonArray FlattenRegexes(JsonArray groups)
    {
        var all = new JsonArray();
        foreach (var item in groups)
        {
            if (item is JsonArray arr)
            {
                foreach (var x in arr) all.Add(x!.DeepClone());
            }
            else if (item is not null)
            {
                all.Add(item.DeepClone());
            }
        }
        return all;
    }

    /// <summary>
    /// Ноды, из которых движку можно выбирать: без служебных, без запрещённых стран,
    /// без выключенных вручную и без слишком медленных.
    /// </summary>
    public static List<ProxyNode> BuildPool(IReadOnlyList<ProxyNode> allNodes, CehoConfig cfg)
    {
        var real = allNodes.Where(n => !n.IsMeta).ToList();

        var byCountry = real
            .Where(n => CountryAllowed(n, cfg))
            .ToList();

        var pool = byCountry
            .Where(n => !IsBlockedByHand(n, cfg))
            .Where(n => !IsTooSlow(n, cfg))
            .ToList();

        if (pool.Count > 0) return pool;

        // Сообщать надо про ту причину, которая опустошила пул, иначе непонятно, что вернуть.
        if (byCountry.Count > 0 && byCountry.All(n => IsBlockedByHand(n, cfg)))
            throw new PoolEmptyException(Strings.T(cfg.Language, "nodes_none_left"));

        if (cfg.MaxLatencyMs is { } limit && real.Any(n => IsTooSlow(n, cfg)))
            throw new PoolEmptyException(Strings.T(cfg.Language, "speed_none_left", limit));

        var key = cfg.PreferredCountries.Count > 0 ? "countries_only_left" : "countries_none_left";
        var reason = cfg.PreferredCountries.Count > 0
            ? string.Join(", ", cfg.PreferredCountries)
            : string.Join(", ", cfg.ExcludedCountries);
        throw new PoolEmptyException(Strings.T(cfg.Language, key, reason));
    }

    private static bool CountryAllowed(ProxyNode node, CehoConfig cfg)
    {
        var code = node.CountryCode ?? CountryResolver.Unknown;
        return !cfg.ExcludedCountries.Contains(code, StringComparer.OrdinalIgnoreCase)
               && (cfg.PreferredCountries.Count == 0
                   || cfg.PreferredCountries.Contains(code, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Нода, выключенная руками в списке.</summary>
    public static bool IsBlockedByHand(ProxyNode node, CehoConfig cfg) =>
        cfg.BlockedNodes.Contains(node.Key, StringComparer.OrdinalIgnoreCase);

    public static bool IsTooSlow(ProxyNode node, CehoConfig cfg) =>
        cfg.MaxLatencyMs is { } limit
        && cfg.NodeLatency.TryGetValue(node.Key, out var ms)
        && ms > limit;

    private static JsonObject BuildTun(CehoConfig cfg)
    {
        // strict_route не включаем нигде. Он ставит на всю машину правила брандмауэра, которые
        // запрещают трафику идти мимо туннеля, — от этого ломаются чужие VPN, локальная сеть
        // и принтеры. Нам чужой трафик держать не надо: свои программы мы узнаём по процессу,
        // а домены берём из подсматривания имени в соединении, поэтому чужой резолвер нам не мешает.
        var tun = new JsonObject
        {
            ["type"] = "tun",
            ["tag"] = "tun-in",
            ["address"] = new JsonArray { cfg.TunAddress },
            ["auto_route"] = true,
            ["stack"] = "gvisor",
        };

        // На Windows имя не задаём намеренно. Своё имя даёт адаптеру устойчивый GUID, Windows
        // помнит для него адрес прошлого запуска и возвращает его при создании — движок падает
        // на «Cannot create a file when that file already exists». Свой адаптер уборка следов
        // узнаёт по записи при запуске, по нашему адресу и по тому, что появилось за этот старт.
        if (Os.IsLinux)
        {
            tun["interface_name"] = TunCleanup.InterfaceName;
            tun["iproute2_table_index"] = TunCleanup.Iproute2TableIndex;
            tun["iproute2_rule_index"] = TunCleanup.Iproute2RuleIndex;
        }

        return tun;
    }

    private static JsonObject BuildDns(string folderRegex, string tunAddress)
    {
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

    private static JsonArray DirectDnsServers(string tunAddress)
    {
        var servers = new JsonArray();
        var system = Os.SystemDnsServers(tunAddress);

        if (system.Count == 0)
        {
            servers.Add(new JsonObject
            {
                ["type"] = "udp", ["tag"] = "dns-direct", ["server"] = Os.PublicResolver,
            });
            return servers;
        }

        for (var i = 0; i < system.Count; i++)
            servers.Add(new JsonObject
            {
                ["type"] = "udp",
                ["tag"] = i == 0 ? "dns-direct" : $"dns-direct-{i + 1}",
                ["server"] = system[i],
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
            new JsonObject
            {
                ["protocol"] = "dns",
                ["process_path_regex"] = new JsonArray { folderRegex },
                ["action"] = "hijack-dns",
            },
            new JsonObject
            {
                ["protocol"] = "dns",
                ["ip_cidr"] = new JsonArray { settings.TunAddress },
                ["action"] = "hijack-dns",
            },
            new JsonObject
            {
                ["inbound"] = new JsonArray { "mixed-in" },
                ["outbound"] = ProxyTag,
            },
        };

        if (blockedTags.Count > 0)
        {
            rules.Add(new JsonObject
            {
                ["process_path_regex"] = new JsonArray { folderRegex },
                ["rule_set"] = blockedTags,
                ["action"] = "reject",
            });
        }

        rules.Add(new JsonObject
        {
            ["process_path_regex"] = new JsonArray { folderRegex },
            ["outbound"] = ProxyTag,
        });

        var route = new JsonObject();
        if (ruleSet.Count > 0)
            route["rule_set"] = ruleSet;
        route["find_process"] = true;
        route["rules"] = rules;
        route["final"] = DirectTag;
        route["auto_detect_interface"] = true;

        route["default_domain_resolver"] = new JsonObject { ["server"] = "dns-direct" };
        return route;
    }

    internal static string FolderPathToRegex(string folderPath)
    {
        var trimmed = folderPath.TrimEnd('\\', '/');
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var lastSep = trimmed.LastIndexOfAny(new[] { '\\', '/' });
            var dir = lastSep >= 0 ? trimmed[..lastSep] : trimmed;
            return "(?i)^(?:" + EscapeRegex(trimmed) + "$|" + EscapeRegex(dir) + @"[\\/])";
        }
        return "(?i)^" + EscapeRegex(trimmed) + @"[\\/]";

        static string EscapeRegex(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in s)
            {
                if ("\\.+*?()|[]{}^$".Contains(ch))
                    sb.Append('\\');
                sb.Append(ch);
            }
            return sb.ToString();
        }
    }
}
