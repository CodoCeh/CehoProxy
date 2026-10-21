package ru.cebers.cehoproxy.core

import org.json.JSONArray
import org.json.JSONObject

enum class SiteMode { ALL_EXCEPT_LIST, ONLY_LIST }
data class RoutePolicy(
    val apps: List<String> = emptyList(),
    val sites: List<String> = emptyList(),
    val siteMode: SiteMode = SiteMode.ALL_EXCEPT_LIST,
    val excludeSelectedApps: Boolean = false,
    val appNodeRoutes: Map<String, String> = emptyMap(),
    val siteNodeRoutes: Map<String, String> = emptyMap(),
    val autoRotationEnabled: Boolean = false,
    val autoRotationIntervalSeconds: Int = 900,
    val autoExcludedRouteIds: Set<String> = emptySet(),
    val excludedCountriesBySubscription: Map<String, Set<String>> = emptyMap(),
    val countryRuleSets: List<CountryRuleSet> = emptyList(),
    val localDpiApps: List<String> = emptyList(),
    val blockYoutubeAds: Boolean = false,
    val disabledSubscriptions: Set<String> = emptySet(),
    val autoDisabledSubscriptions: Set<String> = emptySet(),
    val xrayPorts: Map<String, Int> = emptyMap(),
)

object RoutingConfig {
    fun build(node: ProxyNode, policy: RoutePolicy): String = build(listOf(node), 0, policy)

    fun countryExcluded(node: ProxyNode, excludedCountriesBySubscription: Map<String, Set<String>>): Boolean {
        val country = NodeCountry.resolve(node.name) ?: return false
        return country in excludedCountriesBySubscription[NodePresentation.subscriptionKey(node)].orEmpty()
    }

    fun allowedNodesFor(nodes: List<ProxyNode>, excludedCountriesBySubscription: Map<String, Set<String>>, disabledSubscriptions: Set<String> = emptySet()): List<ProxyNode> {
        val enabled = nodes.filter { NodePresentation.subscriptionKey(it) !in disabledSubscriptions }
        return enabled.filterNot { countryExcluded(it, excludedCountriesBySubscription) }.ifEmpty { enabled }
    }

    fun tagsByRaw(nodes: List<ProxyNode>, excludedCountriesBySubscription: Map<String, Set<String>>, disabledSubscriptions: Set<String> = emptySet()): Map<String, String> {
        val unique = allowedNodesFor(nodes, excludedCountriesBySubscription, disabledSubscriptions).distinctBy { it.raw }
        return unique.mapIndexed { index, node -> node.raw to if (unique.size == 1) "proxy" else "proxy-$index" }.toMap()
    }

    fun build(nodes: List<ProxyNode>, defaultNodeIndex: Int, policy: RoutePolicy): String {
        val dpiApps = directDpiApps(policy)
        require(nodes.isNotEmpty() || dpiApps.isNotEmpty()) { "Нет узлов для маршрутизации" }
        val allowedNodes = allowedNodesFor(nodes, policy.excludedCountriesBySubscription, policy.disabledSubscriptions).let { candidates ->
            candidates.filter { node -> runCatching { outbound(node) }.isSuccess }.ifEmpty { candidates }
        }
        val selectedRouteId = nodes.getOrNull(defaultNodeIndex.coerceIn(0, nodes.lastIndex.coerceAtLeast(0)))?.routeId
        val safeDefaultIndex = allowedNodes.indexOfFirst { it.routeId == selectedRouteId }.coerceAtLeast(0)
        val uniqueNodes = allowedNodes.distinctBy { it.raw }
        val nodeTags = uniqueNodes.mapIndexed { index, _ -> if (uniqueNodes.size == 1) "proxy" else "proxy-$index" }
        val tagByRouteId = uniqueNodes.mapIndexed { index, node -> node.routeId to nodeTags[index] }.toMap()
        val fallbackTag = if (policy.autoRotationEnabled && uniqueNodes.isNotEmpty()) OrderedFailoverConfig.GLOBAL_TAG else tagByRouteId[allowedNodes.getOrNull(safeDefaultIndex)?.routeId] ?: nodeTags.firstOrNull() ?: "block"
        val outbounds = JSONArray()
        uniqueNodes.forEachIndexed { index, node -> outbounds.put(outbound(node, nodeTags[index], policy.xrayPorts)) }
        if (policy.autoRotationEnabled && uniqueNodes.isNotEmpty()) {
            val autoTags = uniqueNodes.indices.filter { index ->
                val node = uniqueNodes[index]
                node.routeId !in policy.autoExcludedRouteIds && NodePresentation.subscriptionKey(node) !in policy.autoDisabledSubscriptions && !countryExcluded(node, policy.excludedCountriesBySubscription)
            }.map { nodeTags[it] }.ifEmpty { nodeTags }
            outbounds.put(OrderedFailoverConfig.selector(OrderedFailoverConfig.GLOBAL_TAG, autoTags))
        }
        if (uniqueNodes.isNotEmpty()) {
            outbounds.put(JSONObject().put("type", "urltest").put("tag", OrderedFailoverConfig.LATENCY_TAG)
                .put("outbounds", JSONArray(nodeTags)).put("url", OrderedFailoverConfig.LATENCY_URL).put("interval", "3m").put("tolerance", 50))
        }
        outbounds.put(JSONObject().put("type", "direct").put("tag", "direct"))
            .put(JSONObject().put("type", "block").put("tag", "block"))
        val rules = JSONArray()
        if (policy.sites.isNotEmpty() || policy.siteNodeRoutes.isNotEmpty() || policy.countryRuleSets.isNotEmpty() || dpiApps.isNotEmpty() || policy.blockYoutubeAds) rules.put(JSONObject().put("action", "sniff"))
        if (policy.blockYoutubeAds) rules.put(JSONObject().put("domain_suffix", JSONArray(YoutubeAdPolicy.domainSuffixes)).put("action", "reject"))
        if (policy.countryRuleSets.isEmpty() && dpiApps.isEmpty()) {
            appendAssignedRules(rules, policy, tagByRouteId)
            appendPolicyRules(rules, policy, fallbackTag)
        } else {
            appendAssignedRules(rules, policy.copy(appNodeRoutes = emptyMap()), tagByRouteId)
            appendPolicyRules(rules, policy, fallbackTag)
            appendLocalDpiRules(rules, dpiApps)
            if (policy.countryRuleSets.isNotEmpty()) rules.put(JSONObject().put("rule_set", JSONArray(policy.countryRuleSets.map { it.tag }))
                .put("action", "route").put("outbound", "direct"))
            appendAssignedRules(rules, policy.copy(siteNodeRoutes = emptyMap()), tagByRouteId)
        }
        val onlySites = policy.siteMode == SiteMode.ONLY_LIST
        return JSONObject().put("log", JSONObject().put("level", "info"))
            .put("dns", dnsConfig(policy, fallbackTag, tagByRouteId))
            .put("inbounds", JSONArray().put(JSONObject().put("type", "tun").put("tag", "tun-in").put("address", JSONArray().put("172.19.0.1/30").put("fdfe:dcba:9876::1/126")).put("auto_route", true).put("strict_route", true).put("stack", "gvisor").apply {
                val includedPackages = (policy.apps + policy.appNodeRoutes.keys).distinct()
                if (!policy.excludeSelectedApps && includedPackages.isNotEmpty()) put("include_package", JSONArray(includedPackages))
                else if (policy.excludeSelectedApps && policy.apps.isNotEmpty()) put("exclude_package", JSONArray(policy.apps))
            }))
            .put("outbounds", outbounds).put("route", JSONObject().put("auto_detect_interface", true).put("default_domain_resolver", "dns-direct").put("rules", rules).apply {
                if (policy.countryRuleSets.isNotEmpty()) put("rule_set", JSONArray(policy.countryRuleSets.map { set ->
                    JSONObject().put("type", "local").put("tag", set.tag).put("format", "binary").put("path", set.path)
                }))
            }.put("final", if (nodes.isEmpty()) "block" else if (onlySites) "direct" else fallbackTag)).toString(2)
    }

    private fun directDpiApps(policy: RoutePolicy): List<String> {
        val apps = LocalDpiPolicy.activeApps(policy.localDpiApps, policy.apps, policy.excludeSelectedApps)
        return if (policy.blockYoutubeAds) (apps + YoutubeAdPolicy.PACKAGE).distinct() else apps
    }

    private fun dnsConfig(policy: RoutePolicy, proxyTag: String, tagByRouteId: Map<String, String>): JSONObject {
        val onlySites = policy.siteMode == SiteMode.ONLY_LIST
        val domains = policy.sites.map(::normalizeDomain).filter(String::isNotBlank).distinct()
        fun remoteServer(tag: String, detour: String) = JSONObject().put("type", "https").put("tag", tag)
            .put("server", "1.1.1.1").put("path", "/dns-query").put("detour", detour)
        val servers = JSONArray().put(remoteServer("dns-remote", proxyTag))
            .put(JSONObject().put("type", "local").put("tag", "dns-direct"))
        val addedServerTags = mutableSetOf("dns-remote")
        val rules = JSONArray()
        policy.siteNodeRoutes.toSortedMap().forEach { (value, nodeRouteId) ->
            val domain = normalizeDomain(value)
            val nodeTag = tagByRouteId[nodeRouteId] ?: return@forEach
            if (domain.isBlank()) return@forEach
            val dnsTag = if (nodeTag == proxyTag) "dns-remote" else "dns-route-$nodeTag"
            if (addedServerTags.add(dnsTag)) servers.put(remoteServer(dnsTag, nodeTag))
            rules.put(JSONObject().put("domain", JSONArray().put(domain)).put("domain_suffix", JSONArray().put(".$domain"))
                .put("server", dnsTag))
        }
        if (domains.isNotEmpty()) rules.put(JSONObject()
            .put("domain", JSONArray(domains)).put("domain_suffix", JSONArray(domains.map { ".$it" }))
            .put("server", if (onlySites) "dns-remote" else "dns-direct"))
        val dpiApps = directDpiApps(policy)
        if (dpiApps.isNotEmpty()) rules.put(JSONObject().put("package_name", JSONArray(dpiApps)).put("server", "dns-direct"))
        val domainSets = policy.countryRuleSets.filter { it.domains }
        if (domainSets.isNotEmpty()) rules.put(JSONObject().put("rule_set", JSONArray(domainSets.map { it.tag }))
            .put("server", "dns-direct"))
        if (proxyTag == "block") rules.put(JSONObject().put("action", "reject"))
        return JSONObject().put("servers", servers)
            .put("rules", rules).put("final", if (onlySites) "dns-direct" else "dns-remote")
            .put("strategy", "ipv4_only")
            .put("reverse_mapping", true)
    }

    fun applyToImported(config: String, policy: RoutePolicy): String {
        if (policy.apps.isEmpty() && !policy.excludeSelectedApps && policy.sites.isEmpty() &&
            policy.appNodeRoutes.isEmpty() && policy.siteNodeRoutes.isEmpty() &&
            !policy.autoRotationEnabled && policy.siteMode == SiteMode.ALL_EXCEPT_LIST) return config
        val root = JSONObject(config)
        val inbounds = root.optJSONArray("inbounds") ?: JSONArray()
        var tun: JSONObject? = null
        for (index in 0 until inbounds.length()) {
            val inbound = inbounds.optJSONObject(index) ?: continue
            if (inbound.optString("type") == "tun") { tun = inbound; break }
        }
        require(tun != null) { "В импортированной конфигурации нет входа TUN" }
        tun!!.put("stack", "gvisor")
        val route = root.optJSONObject("route") ?: JSONObject().also { root.put("route", it) }
        val proxyTag = importedProxyTag(root.optJSONArray("outbounds"), route.optString("final"))
            ?: throw IllegalArgumentException("В импортированной конфигурации нет подходящего proxy outbound для правил сайтов")
        val proxyTags = proxyOutboundTags(root.optJSONArray("outbounds"))
        val tagByRouteId = profileNodeTags(config, proxyTags)
        val fallbackTag = if (policy.autoRotationEnabled) OrderedFailoverConfig.GLOBAL_TAG else proxyTag
        if (policy.autoRotationEnabled) {
            require((0 until root.getJSONArray("outbounds").length()).none {
                root.getJSONArray("outbounds").getJSONObject(it).optString("tag") == OrderedFailoverConfig.GLOBAL_TAG
            }) { "Импортированный профиль использует зарезервированный тег Ceho" }
            val outbounds = root.getJSONArray("outbounds")
            require((0 until outbounds.length()).map { outbounds.getJSONObject(it) }
                .none { it.optString("tag") == "block" && it.optString("type") != "block" }) {
                "Тег block импортированного профиля должен блокировать трафик"
            }
            if ((0 until outbounds.length()).none { outbounds.getJSONObject(it).optString("tag") == "block" }) {
                outbounds.put(JSONObject().put("type", "block").put("tag", "block"))
            }
            outbounds.put(OrderedFailoverConfig.selector(OrderedFailoverConfig.GLOBAL_TAG, proxyTags))
            val dnsServers = root.optJSONObject("dns")?.optJSONArray("servers")
            if (dnsServers != null) for (index in 0 until dnsServers.length()) {
                val server = dnsServers.optJSONObject(index) ?: continue
                if (server.optString("detour") == proxyTag) server.put("detour", fallbackTag)
            }
        }
        tun!!.remove("include_package")
        tun!!.remove("exclude_package")
        val includedPackages = (policy.apps + policy.appNodeRoutes.keys).distinct()
        if (!policy.excludeSelectedApps && includedPackages.isNotEmpty()) tun!!.put("include_package", JSONArray(includedPackages))
        else if (policy.excludeSelectedApps && policy.apps.isNotEmpty()) tun!!.put("exclude_package", JSONArray(policy.apps))
        val rules = JSONArray()
        appendAssignedRules(rules, policy, tagByRouteId)
        appendPolicyRules(rules, policy, fallbackTag)
        val oldRules = route.optJSONArray("rules")
        if (oldRules != null) for (index in 0 until oldRules.length()) {
            val oldRule = oldRules.optJSONObject(index) ?: continue
            if (oldRule.has("package_name")) continue
            if (policy.siteMode == SiteMode.ONLY_LIST) {
                val outbound = oldRule.optString("outbound")
                if (outbound.isNotEmpty() && outbound != "direct" && outbound != "block") continue
            }
            rules.put(oldRule)
        }
        route.put("rules", rules)
        if (policy.siteMode == SiteMode.ONLY_LIST) route.put("final", "direct")
        else if (policy.autoRotationEnabled) route.put("final", OrderedFailoverConfig.GLOBAL_TAG)
        else if (policy.apps.isNotEmpty()) route.put("final", proxyTag)
        return root.toString(2)
    }

    private fun proxyOutboundTags(outbounds: JSONArray?): List<String> = outbounds?.let { array ->
        (0 until array.length()).mapNotNull { array.optJSONObject(it) }
            .filter { it.optString("type") !in setOf("direct", "block", "dns", "selector", "urltest", "loadbalance") }
            .map { it.optString("tag") }.filter(String::isNotBlank)
    }.orEmpty()

    private fun importedProxyTag(outbounds: JSONArray?, preferredTag: String): String? {
        val candidates = proxyOutboundTags(outbounds)
        return candidates.firstOrNull { it == preferredTag } ?: candidates.firstOrNull()
    }

    private fun profileNodeTags(config: String, proxyTags: List<String>): Map<String, String> {
        val nodes = NodeShareParser.parse(config)
        return nodes.mapIndexedNotNull { index, node -> proxyTags.getOrNull(index)?.let { node.routeId to it } }.toMap()
    }

    private fun appendLocalDpiRules(rules: JSONArray, apps: List<String>) {
        if (apps.isEmpty()) return
        rules.put(JSONObject().put("package_name", JSONArray(apps)).put("network", "udp").put("port", JSONArray().put(443))
            .put("action", "reject").put("method", "default").put("no_drop", true))
        rules.put(JSONObject().put("package_name", JSONArray(apps)).put("network", "tcp")
            .put("action", "route").put("outbound", "direct").put("tls_record_fragment", true))
        rules.put(JSONObject().put("package_name", JSONArray(apps)).put("action", "route").put("outbound", "direct"))
    }

    private fun appendAssignedRules(rules: JSONArray, policy: RoutePolicy, tagByRouteId: Map<String, String>) {
        policy.siteNodeRoutes.toSortedMap().forEach { (domainValue, nodeRouteId) ->
            val domain = normalizeDomain(domainValue)
            val tag = tagByRouteId[nodeRouteId] ?: return@forEach
            if (domain.isNotBlank()) rules.put(JSONObject().put("domain", JSONArray().put(domain))
                .put("domain_suffix", JSONArray().put(".$domain")).put("outbound", tag))
        }
        policy.appNodeRoutes.toSortedMap().forEach { (packageName, nodeRouteId) ->
            val tag = tagByRouteId[nodeRouteId] ?: return@forEach
            if (packageName.isNotBlank()) rules.put(JSONObject().put("package_name", JSONArray().put(packageName)).put("outbound", tag))
        }
    }

    private fun appendPolicyRules(rules: JSONArray, policy: RoutePolicy, proxyTag: String = "proxy") {
        val domains = policy.sites.map(::normalizeDomain).filter(String::isNotBlank).distinct()
        if (domains.isNotEmpty()) {
            val siteRule = JSONObject().put("domain", JSONArray(domains))
                .put("domain_suffix", JSONArray(domains.map { ".$it" }))
                .put("outbound", if (policy.siteMode == SiteMode.ONLY_LIST) proxyTag else "direct")
            rules.put(siteRule)
        }
    }

    fun normalizeDomain(value: String): String = value.trim().lowercase().removePrefix("https://").removePrefix("http://").substringBefore('/').substringBefore(':').removePrefix("*.").trimEnd('.')

    private fun outbound(node: ProxyNode, tag: String = "proxy", xrayPorts: Map<String, Int> = emptyMap()): JSONObject {
        if (node.raw.trimStart().startsWith("{")) return JSONObject(node.raw).put("tag", tag)
        if (XrayBridge.needsXray(node)) {
            return JSONObject().put("type", "socks").put("tag", tag).put("server", "127.0.0.1")
                .put("server_port", xrayPorts[node.routeId] ?: 0).put("version", "5")
        }
        val uri = runCatching { java.net.URI(node.raw) }.getOrNull()
        val type = when (node.type) { "ss" -> "shadowsocks"; "socks5", "socks5h" -> "socks"; else -> node.type }
        val result = JSONObject().put("type", type).put("tag", tag).put("server", node.server).put("server_port", node.port)
        val userInfo = uri?.rawUserInfo
        val query = uri?.rawQuery.orEmpty().split('&').mapNotNull { entry ->
            val split = entry.indexOf('=')
            if (split < 0) null else decodeUriPart(entry.substring(0, split)) to decodeUriPart(entry.substring(split + 1))
        }.toMap()
        if (type == "vless") {
            result.put("uuid", decodeUriPart(userInfo.orEmpty().substringBefore(':')))
            query["flow"]?.takeIf(String::isNotBlank)?.let { result.put("flow", it) }
            appendTls(result, node, query, query["security"]?.equals("tls", true) == true || query["security"]?.equals("reality", true) == true)
            appendTransport(result, query)
        }
        if (type == "trojan") {
            result.put("password", decodeUriPart(userInfo.orEmpty()))
            appendTls(result, node, query, forceEnabled = true)
            appendTransport(result, query)
        }
        if (type == "tuic") {
            val auth = userInfo.orEmpty()
            result.put("uuid", decodeUriPart(auth.substringBefore(':')))
            if (':' in auth) result.put("password", decodeUriPart(auth.substringAfter(':')))
            val congestion = query["congestion_control"] ?: query["cc"]
            if (congestion in setOf("cubic", "new_reno", "bbr")) result.put("congestion_control", congestion)
            appendTls(result, node, query, forceEnabled = true)
        }
        if (type == "hysteria2") {
            val auth = userInfo.orEmpty()
            result.put("password", decodeUriPart(if (':' in auth) "${auth.substringBefore(':')}:${auth.substringAfter(':')}" else auth))
            appendTls(result, node, query, forceEnabled = true)
            query["obfs"]?.takeIf(String::isNotBlank)?.let { obfs ->
                require(obfs in setOf("salamander", "gecko")) { "Hysteria2 obfs '$obfs' не поддерживается sing-box" }
                val obfsPassword = query["obfs-password"]
                val validObfsPassword = obfsPassword?.takeIf(String::isNotBlank)
                    ?: throw IllegalArgumentException("Для Hysteria2 obfs требуется obfs-password")
                result.put("obfs", JSONObject().put("type", obfs).put("password", validObfsPassword))
            }
            query["mport"]?.takeIf(String::isNotBlank)?.let { ports ->
                result.put("server_ports", JSONArray(ports.split(',').map { it.trim().replace('-', ':') }.filter(String::isNotBlank)))
            }
        }
        if (type == "vmess") appendVmess(result, node)
        if (type == "socks" || type == "http") {
            if (type == "socks") result.put("version", if (uri?.scheme.equals("socks4", true)) "4" else "5")
            val auth = userInfo.orEmpty()
            if (auth.isNotEmpty()) {
                result.put("username", decodeUriPart(auth.substringBefore(':')))
                if (':' in auth) result.put("password", decodeUriPart(auth.substringAfter(':')))
            }
        }
        if (type == "naive") {
            val credentials = userInfo.orEmpty()
            val delimiter = credentials.indexOf(':')
            result.put("username", decodeUriPart(if (delimiter >= 0) credentials.substring(0, delimiter) else credentials))
                .put("password", if (delimiter >= 0) decodeUriPart(credentials.substring(delimiter + 1)) else "")
            val tls = JSONObject().put("enabled", true)
            val isAddress = node.server.contains(':') || node.server.matches(Regex("\\d{1,3}(?:\\.\\d{1,3}){3}"))
            val sni = query["sni"]?.takeIf(String::isNotBlank) ?: node.server.takeUnless { isAddress }
            if (!sni.isNullOrBlank()) tls.put("server_name", sni)
            if (query["insecure"] == "1" || query["insecure"]?.equals("true", true) == true) tls.put("insecure", true)
            val quic = uri?.scheme.equals("naive+quic", ignoreCase = true)
            result.put("tls", tls).put("domain_strategy", "ipv4_only").put("quic", quic)
                .put("domain_resolver", JSONObject().put("server", "dns-direct").put("strategy", "ipv4_only"))
        }
        if (type == "shadowsocks") {
            require(query["plugin"].isNullOrBlank()) { "SS-плагин не поддерживается на Android" }
            val body = node.raw.substringAfter("ss://").substringBefore('#').substringBefore('?')
            val userInfo = if ('@' in body) body.substringBefore('@') else body
            val auth = runCatching { String(Base64Compat.decode(userInfo), Charsets.UTF_8) }.getOrNull()
                ?.let { decoded -> if ('@' in body) decoded else decoded.substringBeforeLast('@') }
                ?.takeIf { ':' in it } ?: decodeUriPart(userInfo)
            if (':' in auth) result.put("method", auth.substringBefore(':')).put("password", auth.substringAfter(':'))
        }
        return result
    }

    private fun appendTls(result: JSONObject, node: ProxyNode, query: Map<String, String>, forceEnabled: Boolean) {
        val security = query["security"].orEmpty()
        val realityEnabled = security.equals("reality", true)
        val tlsEnabled = forceEnabled || security.equals("tls", true) || realityEnabled || query["tls"].equals("tls", true)
        if (!tlsEnabled) return
        val tls = JSONObject().put("enabled", true)
        val sni = query["sni"]?.takeIf(String::isNotBlank)
            ?: query["peer"]?.takeIf(String::isNotBlank)
            ?: node.server
        tls.put("server_name", sni)
        if (query["allowInsecure"].isTruthy() || query["insecure"].isTruthy()) tls.put("insecure", true)
        query["alpn"]?.split(',')?.map(String::trim)?.filter(String::isNotBlank)?.takeIf { it.isNotEmpty() }
            ?.let { tls.put("alpn", JSONArray(it)) }
        query["fp"]?.takeIf(String::isNotBlank)?.let { tls.put("utls", JSONObject().put("enabled", true).put("fingerprint", it)) }
        if (realityEnabled) {
            val publicKey = query["pbk"] ?: query["publicKey"]
            val shortId = query["sid"] ?: query["shortId"]
            val validPublicKey = publicKey?.takeIf(String::isNotBlank)
                ?: throw IllegalArgumentException("VLESS Reality требует параметр pbk")
            val reality = JSONObject().put("enabled", true)
            reality.put("public_key", validPublicKey)
            shortId?.takeIf(String::isNotBlank)?.let { reality.put("short_id", it) }
            tls.put("reality", reality)
        }
        result.put("tls", tls)
    }

    private fun appendTransport(result: JSONObject, query: Map<String, String>) {
        val network = (query["type"] ?: query["network"] ?: "").lowercase()
        if (network.isBlank() || network == "tcp" || network == "raw") return
        val transportType = when (network) {
            "ws", "websocket" -> "ws"
            "grpc" -> "grpc"
            "http", "h2" -> "http"
            "httpupgrade" -> "httpupgrade"
            "quic" -> "quic"
            else -> throw IllegalArgumentException("V2Ray transport '$network' не поддерживается sing-box")
        }
        val transport = JSONObject().put("type", transportType)
        when (transportType) {
            "ws" -> {
                query["path"]?.let { transport.put("path", it) }
                val host = query["host"]
                if (!host.isNullOrBlank()) transport.put("headers", JSONObject().put("Host", host))
                query["ed"]?.toIntOrNull()?.takeIf { it > 0 }?.let { transport.put("max_early_data", it) }
                query["eh"]?.takeIf(String::isNotBlank)?.let { transport.put("early_data_header_name", it) }
            }
            "grpc" -> (query["serviceName"] ?: query["service_name"])?.let { transport.put("service_name", it) }
            "http" -> {
                query["path"]?.let { transport.put("path", it) }
                query["host"]?.split(',')?.map(String::trim)?.filter(String::isNotBlank)?.takeIf { it.isNotEmpty() }
                    ?.let { transport.put("host", JSONArray(it)) }
            }
            "httpupgrade" -> {
                query["path"]?.let { transport.put("path", it) }
                query["host"]?.let { transport.put("host", it) }
            }
        }
        result.put("transport", transport)
    }

    private fun appendVmess(result: JSONObject, node: ProxyNode) {
        val payload = node.raw.substringAfter("vmess://", "").substringBefore('#')
        val obj = JSONObject(String(Base64Compat.decode(payload), Charsets.UTF_8))
        result.put("server", obj.optString("add", node.server))
            .put("server_port", obj.optInt("port", node.port))
            .put("uuid", obj.optString("id"))
            .put("security", obj.optString("scy", "auto"))
            .put("alter_id", obj.optInt("aid", 0))
        val query = buildMap {
            obj.optString("net").takeIf(String::isNotBlank)?.let { put("type", it) }
            obj.optString("host").takeIf(String::isNotBlank)?.let { put("host", it) }
            obj.optString("path").takeIf(String::isNotBlank)?.let { put("path", it) }
            obj.optString("serviceName").takeIf(String::isNotBlank)?.let { put("serviceName", it) }
            obj.optString("sni").takeIf(String::isNotBlank)?.let { put("sni", it) }
            obj.optString("alpn").takeIf(String::isNotBlank)?.let { put("alpn", it) }
            obj.optString("fp").takeIf(String::isNotBlank)?.let { put("fp", it) }
            obj.optString("tls").takeIf(String::isNotBlank)?.let { put("tls", it) }
        }
        if (obj.optString("tls").equals("tls", true)) appendTls(result, node.copy(server = obj.optString("sni", node.server)), query, forceEnabled = true)
        appendTransport(result, query)
    }

    private fun String?.isTruthy(): Boolean = this == "1" || this.equals("true", ignoreCase = true)

    private fun decodeUriPart(value: String): String = java.net.URLDecoder.decode(value.replace("+", "%2B"), Charsets.UTF_8.name())
}
