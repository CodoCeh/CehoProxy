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

    public static string GenerateForConfig(IReadOnlyList<ProxyNode> allNodes, CehoConfig cfg)
    {
        var apps = cfg.Apps.Where(a => a.Enabled && !string.IsNullOrWhiteSpace(a.Folder)).ToList();
        if (apps.Count == 0)
            throw new InvalidOperationException("Не добавлено ни одного приложения — изолировать нечего.");

        var pool = BuildPool(allNodes, cfg);

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
            ["log"] = BuildLog(cfg),
            ["dns"] = new JsonObject
            {
                ["servers"] = DnsServersWithDirect(cfg.TunAddress),
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
                    new JsonObject { ["protocol"] = "dns", ["action"] = "hijack-dns" },
                    new JsonObject { ["inbound"] = new JsonArray { "mixed-in" }, ["outbound"] = ProxyTag },
                    new JsonObject { ["process_path_regex"] = appRegexes.DeepClone(), ["outbound"] = ProxyTag },
                },
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
        var tun = new JsonObject
        {
            ["type"] = "tun",
            ["tag"] = "tun-in",
            ["address"] = new JsonArray { cfg.TunAddress },
            ["auto_route"] = true,
            ["stack"] = "gvisor",
        };

        if (!Os.IsMac) tun["strict_route"] = true;

        // Имя нужно не для красоты: по нему уборка следов узнаёт наш адаптер и не сносит
        // туннели других VPN-клиентов, которые тоже стоят на Wintun.
        if (!Os.IsMac) tun["interface_name"] = TunCleanup.InterfaceName;

        if (Os.IsLinux)
        {
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
            new JsonObject { ["protocol"] = "dns", ["action"] = "hijack-dns" },
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
        route["rules"] = rules;
        route["final"] = DirectTag;
        route["auto_detect_interface"] = true;

        route["default_domain_resolver"] = new JsonObject { ["server"] = "dns-direct" };
        return route;
    }

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
